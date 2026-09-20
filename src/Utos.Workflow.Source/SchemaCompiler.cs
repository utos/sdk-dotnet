using System;
using System.Collections.Generic;
using System.Linq;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Utos.Workflows.V1.Source;

/// <summary>Which slot is being compiled. Only <see cref="Env"/> behaves differently.</summary>
internal enum SchemaSlot
{
    Input,
    Output,
    Emits,
    Env
}

/// <summary>
/// Compiles the schema short form of <c>api/docs/workflow-schemas.md</c> into plain JSON Schema
/// 2020-12.
/// <para>
/// This happens <strong>only here</strong>. A bundle carries the standard form, so `?`,
/// <c>min</c>/<c>max</c> and the closed-by-default rule are resolved at this edge and nothing
/// downstream — no daemon, no registry, no second SDK — ever learns our spelling.
/// </para>
/// </summary>
internal static class SchemaCompiler
{
    private const string Dialect = "https://json-schema.org/draft/2020-12/schema";

    /// <summary>What a declaration may say <c>type</c> is. The spec owns this list.</summary>
    private static readonly HashSet<string> RegistryTypes = new(StringComparer.Ordinal)
    {
        "string", "number", "integer", "boolean", "object", "array", "null"
    };

    /// <summary>Constraints that apply whatever the declared type is.</summary>
    private static readonly HashSet<string> Common = new(StringComparer.Ordinal)
    {
        "type", "$ref", "description", "nullable", "const", "enum", "default", "title", "examples"
    };

    /// <summary>Constraints by the type they apply to, and the JSON Schema keyword each becomes.</summary>
    private static readonly Dictionary<string, Dictionary<string, string>> ByType = new(StringComparer.Ordinal)
    {
        ["string"] = new(StringComparer.Ordinal)
        {
            ["format"] = "format",
            ["minLength"] = "minLength",
            ["maxLength"] = "maxLength",
            ["pattern"] = "pattern"
        },
        ["number"] = Numeric(),
        ["integer"] = Numeric(),
        ["array"] = new(StringComparer.Ordinal)
        {
            ["items"] = "items",
            ["minItems"] = "minItems",
            ["maxItems"] = "maxItems",
            ["uniqueItems"] = "uniqueItems"
        },
        ["object"] = new(StringComparer.Ordinal)
        {
            ["properties"] = "properties",
            ["open"] = "open",
            ["minProperties"] = "minProperties",
            ["maxProperties"] = "maxProperties"
        },
        ["boolean"] = new(StringComparer.Ordinal),
        ["null"] = new(StringComparer.Ordinal)
    };

    /// <summary>
    /// <c>min</c> and <c>max</c> are shortened because they appear on nearly every numeric field;
    /// everything else keeps its JSON Schema name so what an author learns here transfers.
    /// </summary>
    private static Dictionary<string, string> Numeric() => new(StringComparer.Ordinal)
    {
        ["min"] = "minimum",
        ["max"] = "maximum",
        ["exclusiveMin"] = "exclusiveMinimum",
        ["exclusiveMax"] = "exclusiveMaximum",
        ["multipleOf"] = "multipleOf"
    };

    /// <summary>
    /// Compiles one slot. Returns the node to put in its place — plain JSON Schema, whatever form
    /// the author wrote.
    /// </summary>
    internal static YamlNode Compile(
        YamlNode slot, SchemaSlot kind, string path, string file, List<SourceIssue> issues)
    {
        if (slot is not YamlMappingNode mapping)
        {
            issues.Add(Issue(SourceCodes.SchemaMalformed,
                $"{path} must be a mapping.", file, slot));
            return slot;
        }

        // Form 1: an explicit dialect means the author wrote JSON Schema directly, for what the
        // short form does not reach — oneOf, if/then, tuples. Copied through with `$schema`
        // removed, since the dialect is pinned by the spec and carrying it in every bundle is
        // noise two builds would have to keep identical.
        if (Find(mapping, "$schema") is { } declared)
        {
            if (Scalar(declared) != Dialect)
            {
                issues.Add(Issue(SourceCodes.SchemaMalformed,
                    $"{path}.$schema must be '{Dialect}'.", file, declared));
            }

            return Without(mapping, "$schema");
        }

        // Form 2: a `type` key whose value is the *string* `object`. A field genuinely named
        // `type` carries a mapping, so the two can never be confused.
        if (Find(mapping, "type") is YamlScalarNode { Value: "object" })
            return CompileObject(mapping, kind, path, file, issues);

        // Form 3: a bare field map, which is what almost everything should be.
        return CompileObject(WrapAsObject(mapping), kind, path, file, issues);
    }

    /// <summary>A field map is shorthand for <c>{ type: object, properties: &lt;the map&gt; }</c>.</summary>
    private static YamlMappingNode WrapAsObject(YamlMappingNode fields)
    {
        var wrapped = new YamlMappingNode();
        wrapped.Add(new YamlScalarNode("type"), new YamlScalarNode("object"));
        wrapped.Add(new YamlScalarNode("properties"), fields);
        return wrapped;
    }

    private static YamlNode CompileObject(
        YamlMappingNode declaration, SchemaSlot kind, string path, string file, List<SourceIssue> issues)
    {
        var result = new YamlMappingNode();
        result.Add(new YamlScalarNode("type"), new YamlScalarNode("object"));

        var open = Scalar(Find(declaration, "open")) is "true";
        var required = new List<string>();
        var properties = new YamlMappingNode();

        if (Find(declaration, "properties") is YamlMappingNode fields)
        {
            var seen = new Dictionary<string, YamlNode>(StringComparer.Ordinal);

            foreach (var (key, value) in fields.Children)
            {
                var written = YamlJson.Key(key);
                var optional = written.EndsWith("?", StringComparison.Ordinal);
                var name = optional ? written.Substring(0, written.Length - 1) : written;

                // `x` and `x?` differ as text, so no duplicate-key check objects — and they mean
                // one property, declared both required and optional at once.
                if (seen.ContainsKey(name))
                {
                    issues.Add(Issue(SourceCodes.SchemaDuplicateProperty,
                        $"'{name}' is declared twice in {path}, once required and once optional.",
                        file, key));
                    continue;
                }

                seen[name] = key;
                if (!optional) required.Add(name);

                properties.Add(new YamlScalarNode(name),
                    CompileField(value, kind, $"{path}[\"{name}\"]", file, issues, key));
            }
        }

        // Sorted, not in declaration order: `properties` is a JSON object whose keys the content
        // digest sorts anyway, so leaving `required` order-sensitive would make it the one place
        // reordering unrelated keys changed a workflow's identity.
        if (required.Count > 0)
        {
            required.Sort(StringComparer.Ordinal);
            var list = new YamlSequenceNode();
            foreach (var name in required) list.Add(new YamlScalarNode(name));
            result.Add(new YamlScalarNode("required"), list);
        }

        if (properties.Children.Count > 0) result.Add(new YamlScalarNode("properties"), properties);

        CopyAnnotations(declaration, result);

        // `spec.env` is never closed. It is ambient — shared across a run tree and inherited by
        // every sub-workflow — so closing it would mean a child rejecting every variable its
        // parent needed and it did not.
        if (kind != SchemaSlot.Env && !open)
            result.Add(new YamlScalarNode("unevaluatedProperties"), Plain("false"));

        return result;
    }

    /// <summary>One property's declaration.</summary>
    private static YamlNode CompileField(
        YamlNode declaration, SchemaSlot kind, string path, string file,
        List<SourceIssue> issues, YamlNode at)
    {
        // In spec.env a null value is a required string with no further constraints, which is the
        // common case and deserves the short spelling: `API_BASE:` with nothing after it.
        if (kind == SchemaSlot.Env && declaration is YamlScalarNode { Value: null or "" or "~" or "null" })
            return StringSchema();

        if (declaration is not YamlMappingNode mapping)
        {
            issues.Add(Issue(SourceCodes.SchemaMalformed,
                $"{path} must be a mapping.", file, declaration));
            return new YamlMappingNode();
        }

        var type = Scalar(Find(mapping, "type"));
        var reference = Find(mapping, "$ref");

        if (kind == SchemaSlot.Env)
        {
            // Every property of spec.env is a string, because a run's environment is a map of
            // string to string. Written or not, it compiles to one.
            if (type is not null && type != "string")
            {
                issues.Add(Issue(SourceCodes.SchemaTypeUnknown,
                    $"{path} must be of type 'string'; spec.env is a map of string to string.",
                    file, at));
            }

            type = "string";
        }

        if (type is null && reference is null && kind != SchemaSlot.Env)
        {
            issues.Add(Issue(SourceCodes.SchemaTypeUnknown,
                $"{path} must declare a 'type' or a '$ref'.", file, at));
            return new YamlMappingNode();
        }

        if (type is not null && !RegistryTypes.Contains(type))
        {
            issues.Add(Issue(SourceCodes.SchemaTypeUnknown,
                $"{path} declares type '{type}', which is not in the type registry. "
                + $"Expected one of: {string.Join(", ", RegistryTypes.OrderBy(t => t, StringComparer.Ordinal))}.",
                file, at));
            return new YamlMappingNode();
        }

        var result = new YamlMappingNode();
        var nullable = Scalar(Find(mapping, "nullable")) is "true";

        if (type is not null)
        {
            // `nullable` is separate from `?`: a property may be absent or hold null, and the
            // system distinguishes them — undefined omits a field, null is carried.
            if (nullable)
            {
                var union = new YamlSequenceNode();
                union.Add(new YamlScalarNode(type));
                union.Add(new YamlScalarNode("null"));
                result.Add(new YamlScalarNode("type"), union);
            }
            else
            {
                result.Add(new YamlScalarNode("type"), new YamlScalarNode(type));
            }
        }

        var applicable = type is not null && ByType.TryGetValue(type, out var map)
            ? map
            : new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, value) in mapping.Children)
        {
            var name = YamlJson.Key(key);

            // Handled above, or handled below as a nested structure.
            if (name is "type" or "nullable" or "properties" or "items" or "open") continue;

            if (name == "$ref")
            {
                result.Add(new YamlScalarNode("$ref"), value);
                continue;
            }

            if (Common.Contains(name))
            {
                result.Add(new YamlScalarNode(name), value);
                continue;
            }

            if (applicable.TryGetValue(name, out var keyword))
            {
                result.Add(new YamlScalarNode(keyword), value);
                continue;
            }

            issues.Add(Issue(SourceCodes.SchemaConstraintUnknown,
                $"{path} declares '{name}', which is not a constraint of type '{type}'.",
                file, key));
        }

        if (type == "object")
        {
            var nested = CompileObject(mapping, kind, path, file, issues);
            return Merge(result, (YamlMappingNode)nested);
        }

        if (type == "array" && Find(mapping, "items") is { } items)
        {
            result.Add(new YamlScalarNode("items"),
                CompileField(items, kind, $"{path}.items", file, issues, items));
        }

        return result;
    }

    /// <summary>
    /// A scalar that resolves to its JSON type rather than to a string.
    /// <para>
    /// A constructed <see cref="YamlScalarNode"/> defaults to <see cref="ScalarStyle.Any"/>, and
    /// only a <em>plain</em> one is resolved — a quoted scalar is a string by construction. So
    /// <c>unevaluatedProperties</c> emitted without this becomes the string <c>"false"</c>, which
    /// is a truthy schema rather than a closed object: the opposite of what it says.
    /// </para>
    /// </summary>
    private static YamlScalarNode Plain(string value) =>
        new(value) { Style = ScalarStyle.Plain };

    private static YamlMappingNode StringSchema()
    {
        var schema = new YamlMappingNode();
        schema.Add(new YamlScalarNode("type"), new YamlScalarNode("string"));
        return schema;
    }

    /// <summary>
    /// Folds a nested object's compiled body into the property's own, without duplicating the
    /// <c>type</c> both sides emit.
    /// </summary>
    private static YamlMappingNode Merge(YamlMappingNode property, YamlMappingNode body)
    {
        var merged = new YamlMappingNode();
        foreach (var (key, value) in property.Children) merged.Add(key, value);

        foreach (var (key, value) in body.Children)
        {
            var name = YamlJson.Key(key);
            if (name == "type" && merged.Children.Keys.Any(k => YamlJson.Key(k) == "type")) continue;
            if (merged.Children.Keys.Any(k => YamlJson.Key(k) == name)) continue;

            merged.Add(key, value);
        }

        return merged;
    }

    /// <summary>Annotations an object declaration may carry beside its properties.</summary>
    private static void CopyAnnotations(YamlMappingNode declaration, YamlMappingNode result)
    {
        foreach (var name in new[] { "description", "title" })
        {
            if (Find(declaration, name) is { } value)
                result.Add(new YamlScalarNode(name), value);
        }
    }

    private static YamlMappingNode Without(YamlMappingNode mapping, string key)
    {
        var result = new YamlMappingNode();
        foreach (var (k, v) in mapping.Children)
        {
            if (YamlJson.Key(k) == key) continue;
            result.Add(k, v);
        }

        return result;
    }

    private static YamlNode? Find(YamlMappingNode mapping, string key)
    {
        foreach (var (k, v) in mapping.Children)
        {
            if (YamlJson.Key(k) == key) return v;
        }

        return null;
    }

    private static string? Scalar(YamlNode? node) =>
        node is YamlScalarNode { Style: ScalarStyle.Plain or ScalarStyle.SingleQuoted or ScalarStyle.DoubleQuoted } scalar
            ? scalar.Value
            : null;

    private static SourceIssue Issue(string code, string message, string file, YamlNode at) =>
        new(code, message, file, (int)at.Start.Line, (int)at.Start.Column);
}
