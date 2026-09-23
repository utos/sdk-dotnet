using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf.Reflection;
using YamlDotNet.RepresentationModel;

namespace Utos.Workflows.V1.Source;

/// <summary>
/// Rewrites a rule's <c>return</c> action into the wire's <c>result</c>, as
/// <c>api/docs/workflow-source-format.md</c> § Mapping specifies.
/// <para>
/// The wire keeps the name <c>result</c> because <c>return</c> is a reserved word in several
/// target languages — a generated <c>msg.return</c> is a syntax error in Python — and the source
/// format spells it <c>return</c> because that is what ending a path with a value is. A bare
/// <c>return</c> — <c>- return</c>, <c>- return:</c>, <c>- return: ~</c> — ends the path with no
/// value and becomes an empty struct: proto3 JSON reads <c>"result": null</c> as <em>unset</em>,
/// which would be a rule with no action at all. <c>result</c> in a source document is refused
/// (<c>UTOS-S009</c>), so the two spellings never coexist and the wire name never leaks into
/// authored files.
/// </para>
/// <para>
/// A bare <c>error</c> is read the same way (spec 0.0.17) and becomes an empty
/// <c>WorkflowError</c>: the re-raise, which fails the path with the failure being handled.
/// <c>error</c> keeps its name on the wire. Where it is legal — <c>onFailure</c> only — is the
/// shared validator's rule (<c>UTOS-T005</c>), not this mapping's.
/// </para>
/// </summary>
internal static class RuleTransform
{
    private const string SourceKey = "return";
    private const string WireKey = "result";
    private const string ErrorKey = "error";

    /// <summary>
    /// A rule's effect key may be dotted — <c>workflow.call</c> — and resolves by the same
    /// walk an activity's <c>type</c> does: each segment names a field in the oneof the
    /// message currently reached declares, starting at <see cref="TransitionRule"/>'s
    /// <c>effect</c>. Derived from the descriptor rather than listed here, so an effect added
    /// to the proto becomes authorable with no change to this mapping.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> EffectPaths =
        BuildEffectPaths();

    private static Dictionary<string, IReadOnlyList<string>> BuildEffectPaths()
    {
        var paths = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var oneof in TransitionRule.Descriptor.Oneofs)
        {
            if (oneof.IsSynthetic || oneof.Name != "effect") continue;

            foreach (var field in oneof.Fields) Walk(field, [], paths);
        }

        return paths;

        // A segment that lands on a message declaring a oneof of its own continues; one that
        // does not is a complete key. `emit` ends immediately (its value is a Struct), while
        // `workflow` continues into call.
        static void Walk(FieldDescriptor field, List<FieldDescriptor> prefix,
            Dictionary<string, IReadOnlyList<string>> into)
        {
            prefix.Add(field);

            var nested = field.FieldType == FieldType.Message
                ? field.MessageType.Oneofs.Where(o => !o.IsSynthetic).ToList()
                : [];

            if (nested.Count == 0)
            {
                var segments = prefix.Select(p => p.JsonName).ToArray();
                foreach (var spelling in Spellings(prefix, 0)) into[spelling] = segments;
            }
            else
            {
                foreach (var oneof in nested)
                    foreach (var inner in oneof.Fields)
                        Walk(inner, prefix, into);
            }

            prefix.RemoveAt(prefix.Count - 1);
        }

        // Both spellings proto3 JSON accepts, per segment, as the parser requirements ask.
        static IEnumerable<string> Spellings(List<FieldDescriptor> path, int index)
        {
            var field = path[index];
            string[] names = field.Name == field.JsonName
                ? [field.JsonName]
                : [field.Name, field.JsonName];

            if (index == path.Count - 1)
            {
                foreach (var name in names) yield return name;
                yield break;
            }

            foreach (var name in names)
                foreach (var rest in Spellings(path, index + 1))
                    yield return name + "." + rest;
        }
    }

    /// <summary>The activity-level keys whose values are rule lists, in both spellings proto3 JSON accepts.</summary>
    private static readonly IReadOnlyDictionary<string, string> RuleLists = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["onSuccess"] = "onSuccess", ["on_success"] = "onSuccess",
        ["onFailure"] = "onFailure", ["on_failure"] = "onFailure",
        // Declared by CallActivityConfig, but authored flat beside `type: workflow.call`, so it
        // is an activity-level key here; ActivityTransform nests it afterwards.
        ["onEmitted"] = "onEmitted", ["on_emitted"] = "onEmitted",
    };

    /// <summary>
    /// Returns a copy of <paramref name="activity"/> with every rule list rewritten. Issues are
    /// accumulated rather than thrown so one run reports every bad rule.
    /// </summary>
    public static YamlMappingNode Rewrite(YamlMappingNode activity, string activityName, string file,
        List<SourceIssue> issues)
    {
        var rewritten = new YamlMappingNode();
        foreach (var (key, value) in activity.Children)
        {
            if (RuleLists.TryGetValue(YamlJson.Key(key), out var canonical) && value is YamlSequenceNode rules)
            {
                var path = $"spec.activities[\"{activityName}\"].{canonical}";
                rewritten.Add(key, RewriteRules(rules, path, file, issues));
            }
            else
            {
                rewritten.Add(key, value);
            }
        }

        return rewritten;
    }

    private static YamlSequenceNode RewriteRules(YamlSequenceNode rules, string path, string file,
        List<SourceIssue> issues)
    {
        var rewritten = new YamlSequenceNode();
        var index = 0;
        foreach (var rule in rules.Children)
        {
            rewritten.Add(RewriteRule(rule, $"{path}[{index}]", file, issues));
            index++;
        }

        return rewritten;
    }

    private static YamlNode RewriteRule(YamlNode rule, string path, string file, List<SourceIssue> issues)
    {
        // `- return` and `- error`: a bare scalar item, the whole rule. The only scalars a rule may be.
        if (rule is YamlScalarNode { Value: SourceKey, Style: YamlDotNet.Core.ScalarStyle.Plain })
            return new YamlMappingNode { { new YamlScalarNode(WireKey), new YamlMappingNode() } };
        if (rule is YamlScalarNode { Value: ErrorKey, Style: YamlDotNet.Core.ScalarStyle.Plain })
            return new YamlMappingNode { { new YamlScalarNode(ErrorKey), new YamlMappingNode() } };

        if (rule is not YamlMappingNode mapping)
            return rule;

        var rewritten = new YamlMappingNode();
        foreach (var (key, value) in mapping.Children)
        {
            switch (YamlJson.Key(key))
            {
                case WireKey:
                    // A valid wire field, so proto3 JSON would accept it silently; refused here
                    // so an authored document has one spelling.
                    issues.Add(new SourceIssue(SourceCodes.DocumentMalformed,
                        "'result' is not a source-format key; write 'return' — with a value, or bare to end the path with none.",
                        file, (int)key.Start.Line, (int)key.Start.Column, $"{path}.{WireKey}"));
                    rewritten.Add(key, value);
                    break;

                case SourceKey:
                    rewritten.Add(new YamlScalarNode(WireKey), IsNoValue(value) ? new YamlMappingNode() : value);
                    break;

                case ErrorKey:
                    rewritten.Add(key, IsNoValue(value) ? new YamlMappingNode() : value);
                    break;

                default:
                    // A dotted effect key nests under the oneof fields that reach it, the
                    // way an activity's `type` path does: `workflow.call: {...}` becomes
                    // `workflow: { call: {...} }`. An unknown dotted key is left alone and
                    // surfaces as the unknown field it is when proto3 JSON reads the result.
                    if (EffectPaths.TryGetValue(YamlJson.Key(key), out var segments)
                        && segments.Count > 1)
                    {
                        YamlNode nested = value;
                        for (var level = segments.Count - 1; level >= 1; level--)
                        {
                            nested = new YamlMappingNode
                            {
                                { new YamlScalarNode(segments[level]), nested },
                            };
                        }

                        rewritten.Add(new YamlScalarNode(segments[0]), nested);
                        break;
                    }

                    rewritten.Add(key, value);
                    break;
            }
        }

        return rewritten;
    }

    /// <summary>
    /// <c>return:</c> with nothing after it, <c>return: ~</c>, <c>return: null</c> — every way YAML
    /// spells "no value". A quoted <c>"null"</c> is a string, and is left to fail as one.
    /// </summary>
    private static bool IsNoValue(YamlNode value) =>
        value is YamlScalarNode { Style: YamlDotNet.Core.ScalarStyle.Plain } scalar
        && (string.IsNullOrEmpty(scalar.Value) || scalar.Value is "~" or "null" or "Null" or "NULL");
}
