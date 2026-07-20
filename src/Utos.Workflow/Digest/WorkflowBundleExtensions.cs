namespace Utos.Workflow.V1
{
    /// <summary>Convenience extension methods on <see cref="WorkflowBundle"/>.</summary>
    public static class WorkflowBundleExtensions
    {
        /// <summary>
        /// Computes this bundle's canonical content digest (<c>"sha256:&lt;hex&gt;"</c>).
        /// Shorthand for <see cref="ContentDigest.Compute(WorkflowBundle)"/>.
        /// </summary>
        public static string ComputeContentDigest(this WorkflowBundle bundle) =>
            ContentDigest.Compute(bundle);
    }
}
