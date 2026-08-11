namespace Utos.Workflows.V1
{
    /// <summary>
    /// The fixed values of a workflow document's envelope. Rules <c>UTOS-D001</c> and
    /// <c>UTOS-D002</c> in <c>api/docs/workflow-validation.md</c>.
    /// </summary>
    public static class WorkflowDocument
    {
        /// <summary>
        /// The only recognised <c>Workflow.api_version</c>. A group/version pair mirroring the proto
        /// package major — deliberately not a semantic version, and distinct from
        /// <c>WorkflowMetadata.version</c>.
        /// </summary>
        public const string ApiVersion = "utos.io/v1";

        /// <summary>The only recognised <c>Workflow.kind</c>.</summary>
        public const string Kind = "Workflow";
    }
}
