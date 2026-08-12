using System.Collections.Generic;
using System.Linq;
using Google.Protobuf.Reflection;
using Utos.Workflows.V1;
using Xunit;

namespace Utos.Workflow.Tests;

/// <summary>
/// Guards the descriptor invariants that the source format's <c>type</c> discriminator depends on
/// (see <c>api/docs/workflow-source-format.md</c>, "Normative mapping").
/// <para>
/// The mapping is deliberately descriptor-driven rather than a maintained list: <c>type</c> is a
/// dot-separated path walked through nested <c>config</c> oneofs, and every remaining authored key
/// is placed on whichever message along that path declares a field of that name. That last step is
/// only unambiguous while no field name is declared at two levels of one path — a property of the
/// protos, not of any parser, so it is asserted here rather than trusted to review.
/// </para>
/// </summary>
public class ActivityDescriptorTests
{
    [Fact]
    public void No_field_name_is_declared_at_two_levels_of_one_type_path()
    {
        var collisions = new List<string>();

        foreach (var path in ResolvablePaths(WorkflowActivity.Descriptor, ""))
        {
            var seen = new Dictionary<string, string>();

            foreach (var message in path.Messages)
            {
                foreach (var field in message.Fields.InDeclarationOrder())
                {
                    // Oneof members are path segments, not payload keys — they are selected by
                    // `type`, never authored as sibling keys, so they cannot collide this way.
                    if (field.ContainingOneof is { IsSynthetic: false }) continue;

                    foreach (var spelling in new[] { field.Name, field.JsonName })
                    {
                        if (seen.TryGetValue(spelling, out var owner) && owner != message.Name)
                        {
                            collisions.Add(
                                $"type '{path.Type}': '{spelling}' is declared by both " +
                                $"{owner} and {message.Name}");
                        }
                        else
                        {
                            seen[spelling] = message.Name;
                        }
                    }
                }
            }
        }

        Assert.Empty(collisions);
    }

    [Fact]
    public void Every_type_path_ends_at_a_message_with_no_further_oneof()
    {
        // A path that stops while its message still declares a oneof is incomplete — that is what
        // makes bare `type: workflow` a UTOS-S007 error rather than a defaulted `workflow.call`.
        foreach (var path in ResolvablePaths(WorkflowActivity.Descriptor, ""))
        {
            var leaf = path.Messages[^1];
            Assert.DoesNotContain(leaf.Oneofs, o => !o.IsSynthetic);
        }
    }

    [Fact]
    public void Resolvable_paths_are_exactly_the_documented_set()
    {
        // Documentation, pinned. api/docs/workflow-source-format.md tabulates these; if a kind is
        // added or a mode renamed, this fails and the table gets updated with it.
        var expected = new[]
        {
            "http",
            "promise.all",
            "promise.any",
            "promise.count",
            "promise.race",
            "timer",
            "workflow.call",
            "workflow.spawn",
        };

        var actual = ResolvablePaths(WorkflowActivity.Descriptor, "")
            .Select(p => p.Type)
            .OrderBy(t => t, System.StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    private readonly record struct TypePath(string Type, IReadOnlyList<MessageDescriptor> Messages);

    /// <summary>
    /// Walks every complete <c>type</c> path from a message, mirroring step 1 of the normative
    /// mapping: descend each non-synthetic oneof field, and stop where no oneof remains.
    /// </summary>
    private static IEnumerable<TypePath> ResolvablePaths(MessageDescriptor message, string prefix)
    {
        var oneofs = message.Oneofs.Where(o => !o.IsSynthetic).ToList();

        if (oneofs.Count == 0)
        {
            yield return new TypePath(prefix, new[] { message });
            yield break;
        }

        foreach (var oneof in oneofs)
        {
            foreach (var field in oneof.Fields)
            {
                var segment = prefix.Length == 0 ? field.JsonName : prefix + "." + field.JsonName;

                foreach (var tail in ResolvablePaths(field.MessageType, segment))
                {
                    yield return new TypePath(
                        tail.Type,
                        new[] { message }.Concat(tail.Messages).ToList());
                }
            }
        }
    }
}
