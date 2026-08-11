namespace Utos.Workflows.V1.Validation
{
    /// <summary>
    /// The stable rule identifiers defined by <c>api/docs/workflow-validation.md</c>. Exposed so
    /// callers can branch on a specific rule — or suppress one — without matching on message text.
    /// </summary>
    public static class ValidationCodes
    {
#pragma warning disable CS1591 // Each constant is documented by the spec it mirrors.

        // Bundle
        public const string EntryPointRequired = "UTOS-B001";
        public const string WorkflowsRequired = "UTOS-B002";
        public const string EntryPointNotFound = "UTOS-B003";
        public const string WorkflowKeyRequired = "UTOS-B004";
        public const string WorkflowKeyMismatch = "UTOS-B005";
        public const string SubWorkflowNotInBundle = "UTOS-B006";
        public const string DependenciesNotEmptied = "UTOS-B007";

        // Document envelope
        public const string ApiVersionInvalid = "UTOS-D001";
        public const string KindInvalid = "UTOS-D002";
        public const string MetadataRequired = "UTOS-D003";
        public const string SpecRequired = "UTOS-D004";
        public const string ActivitiesRequired = "UTOS-D005";

        // Metadata
        public const string NameRequired = "UTOS-M001";
        public const string NameTooLong = "UTOS-M002";
        public const string NameInvalid = "UTOS-M003";
        public const string VersionRequired = "UTOS-M004";
        public const string VersionInvalid = "UTOS-M005";
        public const string DescriptionTooLong = "UTOS-M006";
        public const string NamespaceInvalid = "UTOS-M007";
        public const string RegistryInvalid = "UTOS-M008";
        public const string RegistryRequiresNamespace = "UTOS-M009";

        // Activities
        public const string ActivityNameRequired = "UTOS-A001";
        public const string ActivityNameTooLong = "UTOS-A002";
        public const string ActivityNameReserved = "UTOS-A003";
        public const string ActivityNameInvalid = "UTOS-A004";
        public const string ActivityNameBadStart = "UTOS-A005";
        public const string ActivityNameBadEnd = "UTOS-A006";
        public const string ActivityConfigRequired = "UTOS-A007";

        // Transitions
        public const string TransitionActionRequired = "UTOS-T001";
        public const string TransitionTargetRequired = "UTOS-T002";
        public const string TransitionTargetUnresolved = "UTOS-T003";

        // HTTP configuration
        public const string HttpUrlRequired = "UTOS-C101";
        public const string HttpUrlInvalid = "UTOS-C102";
        public const string HttpMethodRequired = "UTOS-C103";

        // Timer configuration
        public const string TimerDurationRequired = "UTOS-C201";
        public const string TimerDurationNotPositive = "UTOS-C202";

        // Promise configuration
        public const string PromiseModeInvalid = "UTOS-C301";
        public const string PromiseRequiredCountInvalid = "UTOS-C302";
        public const string PromiseBranchesRequired = "UTOS-C303";
        public const string PromiseBranchNameRequired = "UTOS-C304";
        public const string PromiseBranchTargetRequired = "UTOS-C305";
        public const string PromiseForEachIncomplete = "UTOS-C306";

        // Sub-workflow configuration
        public const string SubWorkflowRequired = "UTOS-C401";
        public const string SubWorkflowStartActivityRequired = "UTOS-C402";
        public const string SubWorkflowStartActivityUnresolved = "UTOS-C403";

        // Struct values
        public const string NonFiniteNumber = "UTOS-V001";

#pragma warning restore CS1591
    }
}
