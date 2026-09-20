using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Google.Protobuf;
using Utos.Workflows.V1;
using YamlDotNet.RepresentationModel;

namespace Utos.Workflows.V1.Source;

/// <summary>Reads one authored workflow file into its protobuf form.</summary>
public static class WorkflowLoader
{
    /// <summary>Loads and parses <paramref name="path"/>.</summary>
    /// <exception cref="WorkflowSourceException">The file cannot be read or does not parse.</exception>
    public static Workflow LoadFile(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new WorkflowSourceException(new SourceIssue(
                SourceCodes.DependencyFileUnreadable, ex.Message, path));
        }

        return Parse(text, path);
    }

    /// <summary>Parses authored YAML into a <see cref="Workflow"/>.</summary>
    /// <exception cref="WorkflowSourceException">The document does not parse.</exception>
    public static Workflow Parse(string text, string file)
    {
        var root = YamlJson.LoadDocument(text, file);

        var issues = new List<SourceIssue>();
        var transformed = RewriteActivities(root, file, issues);
        if (issues.Count > 0) throw new WorkflowSourceException(issues);

        var json = YamlJson.ToJson(transformed) as JsonObject
                   ?? throw new WorkflowSourceException(new SourceIssue(
                       SourceCodes.DocumentMalformed, "The document root must be a mapping.", file));

        try
        {
            // Unknown fields are rejected, which is what turns this parse into a schema check:
            // a misspelled key is a mistake, not a comment.
            return JsonParser.Default.Parse<Workflow>(json.ToJsonString());
        }
        catch (Exception ex) when (ex is InvalidProtocolBufferException or InvalidJsonException)
        {
            throw new WorkflowSourceException(new SourceIssue(
                SourceCodes.DocumentMalformed, Tidy(ex.Message), file));
        }
    }

    /// <summary>
    /// Applies <see cref="ActivityTransform"/> to every entry of <c>spec.activities</c>, leaving
    /// the rest of the tree alone — outside activities the source shape already matches the proto.
    /// </summary>
    private static YamlMappingNode RewriteActivities(YamlMappingNode root, string file,
        List<SourceIssue> issues)
    {
        if (Child(root, "spec") is not YamlMappingNode spec) return root;
        if (Child(spec, "activities") is not YamlMappingNode activities) return root;

        var rewrittenActivities = new YamlMappingNode();
        foreach (var (key, value) in activities.Children)
        {
            var name = YamlJson.Key(key);
            if (value is YamlMappingNode activity)
            {
                // Rules first, while onEmitted is still an activity-level key; the type transform
                // then nests it under the call configuration.
                var withRules = RuleTransform.Rewrite(activity, name, file, issues);

                // Then the schema, while `schema` is still an activity-level key — it is a field
                // of WorkflowActivity outside the config oneof, so the type transform leaves it
                // alone, but its *contents* are the short form until this runs.
                var withSchema = RewriteActivitySchema(withRules, name, file, issues);

                rewrittenActivities.Add(key, ActivityTransform.Rewrite(withSchema, name, file, issues));
            }
            else
            {
                issues.Add(new SourceIssue(SourceCodes.DocumentMalformed,
                    $"Activity '{name}' must be a mapping.", file, (int)value.Start.Line, (int)value.Start.Column));
            }
        }

        var rewrittenSpec = Replace(spec, "activities", rewrittenActivities);
        rewrittenSpec = RewriteSlot(rewrittenSpec, "output", SchemaSlot.Output, file, issues);
        rewrittenSpec = RewriteSlot(rewrittenSpec, "emits", SchemaSlot.Emits, file, issues);
        rewrittenSpec = RewriteSlot(rewrittenSpec, "env", SchemaSlot.Env, file, issues);

        return Replace(root, "spec", rewrittenSpec);
    }

    /// <summary>
    /// Compiles an activity's <c>schema.input</c>, where it declares one. An absent schema stays
    /// absent: that is the empty schema, which anything satisfies, and is what every workflow
    /// written before schemas existed relies on.
    /// </summary>
    private static YamlMappingNode RewriteActivitySchema(
        YamlMappingNode activity, string name, string file, List<SourceIssue> issues)
    {
        if (Child(activity, "schema") is not YamlMappingNode schema) return activity;
        if (Child(schema, "input") is not { } input) return activity;

        var compiled = SchemaCompiler.Compile(
            input, SchemaSlot.Input, $"activity '{name}' schema.input", file, issues);

        return Replace(activity, "schema", Replace(schema, "input", compiled));
    }

    /// <summary>Compiles one of the workflow-level slots, where the document declares it.</summary>
    private static YamlMappingNode RewriteSlot(
        YamlMappingNode spec, string key, SchemaSlot kind, string file, List<SourceIssue> issues)
    {
        if (Child(spec, key) is not { } slot) return spec;

        return Replace(spec, key, SchemaCompiler.Compile(slot, kind, "spec." + key, file, issues));
    }

    private static YamlNode? Child(YamlMappingNode mapping, string key)
    {
        foreach (var (k, v) in mapping.Children)
            if (YamlJson.Key(k) == key)
                return v;

        return null;
    }

    private static YamlMappingNode Replace(YamlMappingNode mapping, string key, YamlNode replacement)
    {
        var result = new YamlMappingNode();
        foreach (var (k, v) in mapping.Children)
            result.Add(k, YamlJson.Key(k) == key ? replacement : v);

        return result;
    }

    /// <summary>
    /// protobuf reports unknown fields as a bare exception message; prefix it so the reader knows
    /// which layer rejected the document.
    /// </summary>
    private static string Tidy(string message) =>
        message.StartsWith("Unknown field", StringComparison.Ordinal)
            ? message + " (fields are checked against the workflow schema)"
            : message;
}
