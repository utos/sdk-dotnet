using Utos.Workflows.V1;
using Xunit;

namespace Utos.Workflows.Validation.Tests;

/// <summary>
/// Pins the protobuf package names, which are the wire contract.
/// <para>
/// The C# namespace (<c>Utos.Workflows.V1</c>) and the proto package (<c>utos.workflow.v1</c>) are
/// independent: the first is an <c>option csharp_namespace</c>, the second is the identity every
/// implementation in every language agrees on. Renaming the namespace must never move the package,
/// because that would silently change the wire format. These tests make that mistake loud.
/// </para>
/// </summary>
public class WirePackageTests
{
    [Theory]
    [InlineData("utos.workflow.v1.Workflow")]
    [InlineData("utos.workflow.v1.WorkflowBundle")]
    [InlineData("utos.workflow.v1.WorkflowSpec")]
    [InlineData("utos.workflow.v1.WorkflowMetadata")]
    [InlineData("utos.workflow.v1.WorkflowActivity")]
    [InlineData("utos.workflow.v1.WorkflowError")]
    public void Workflow_messages_keep_their_wire_names(string expected)
    {
        var names = new[]
        {
            Workflow.Descriptor.FullName,
            WorkflowBundle.Descriptor.FullName,
            WorkflowSpec.Descriptor.FullName,
            WorkflowMetadata.Descriptor.FullName,
            WorkflowActivity.Descriptor.FullName,
            WorkflowError.Descriptor.FullName,
        };

        Assert.Contains(expected, names);
    }

    [Fact]
    public void Workflow_package_is_not_the_csharp_namespace()
    {
        // The two differ deliberately — "workflow" singular on the wire, "Workflows" plural in C#
        // so the namespace cannot shadow the `Workflow` type for consumers under `Utos.*`.
        Assert.Equal("utos.workflow.v1", Workflow.Descriptor.File.Package);
        Assert.Equal("Utos.Workflows.V1", typeof(Workflow).Namespace);
    }
}
