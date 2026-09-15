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
        // UTOS-A003 (an activity may not be named `end` or `error`) is RETIRED in 0.0.16: those
        // were transition-target keywords, and ending or failing a path is now a `result` or
        // `error` action, so nothing is reserved. Burned, not reused, like UTOS-C301.
        public const string ActivityNameInvalid = "UTOS-A004";
        public const string ActivityNameBadStart = "UTOS-A005";
        public const string ActivityNameBadEnd = "UTOS-A006";
        public const string ActivityConfigRequired = "UTOS-A007";

        // Transitions
        public const string TransitionActionRequired = "UTOS-T001";
        public const string TransitionTargetRequired = "UTOS-T002";
        public const string TransitionTargetUnresolved = "UTOS-T003";
        public const string EmitTransitionRequired = "UTOS-T004";

        /// <summary>
        /// An <c>error</c> action carries no <c>code</c>. The code is what a consumer or an
        /// <c>onFailure</c> rule matches on, so it is a literal and required; <c>message</c> and
        /// <c>details</c> are templates and may be omitted.
        /// </summary>
        public const string ErrorCodeRequired = "UTOS-T005";

        // HTTP configuration
        public const string HttpUrlRequired = "UTOS-C101";
        public const string HttpUrlInvalid = "UTOS-C102";
        public const string HttpMethodRequired = "UTOS-C103";

        // Timer configuration
        public const string TimerDurationRequired = "UTOS-C201";
        public const string TimerDurationNotPositive = "UTOS-C202";

        // Promise configuration.
        // UTOS-C301 (promise mode must be one of all/any/race/count) is RETIRED: the completion
        // mode is a oneof, so an unknown mode is unrepresentable and an unset one is UTOS-A007.
        // The code stays burned rather than reused — codes are a stable contract, and recycling
        // one would silently change the meaning of a suppression somebody already wrote down.
        public const string PromiseRequiredCountInvalid = "UTOS-C302";
        public const string PromiseBranchesRequired = "UTOS-C303";
        public const string PromiseBranchNameRequired = "UTOS-C304";
        // UTOS-C305 (a branch target is required) is RETIRED: a branch dispatches a document, so
        // there is no `target` left to require. What it names is UTOS-C501-C503 now. Burned for
        // the same reason UTOS-C301 is.
        public const string PromiseForEachIncomplete = "UTOS-C306";
        public const string PromiseBranchNameDuplicate = "UTOS-C307";

        // Sub-workflow configuration
        public const string SubWorkflowRequired = "UTOS-C401";
        public const string SubWorkflowStartActivityRequired = "UTOS-C402";
        public const string SubWorkflowStartActivityUnresolved = "UTOS-C403";
        // UTOS-C404 (an onEmitted rule's action must be a transition) is RETIRED: an onEmitted
        // rule is no longer a transition rule, so there is no action left to narrow. What it
        // protected against now holds by construction — a handler is a document, so it cannot
        // transition into the consumer's flow and has no `result` to end the consumer with.
        // UTOS-C405 stays burned too: it never existed as a rule, only as a wrong citation in the
        // proto, and 0.0.13 tells readers so.

        // Dispatch — a promise branch or an onEmitted rule. One range for both, because they
        // carry the same three fields and mean the same thing by them; two identical rules under
        // different codes would duplicate everything a code is for.
        public const string DispatchWorkflowRequired = "UTOS-C501";
        public const string DispatchStartActivityRequired = "UTOS-C502";
        public const string DispatchStartActivityUnresolved = "UTOS-C503";

        /// <summary>
        /// An onEmitted rule carries no action. Weak by design: the actions are a proto oneof, so
        /// two cannot be expressed and only "none" is left to check — the same thing UTOS-T001
        /// checks for a transition rule.
        /// </summary>
        public const string EmissionRuleActionRequired = "UTOS-C504";

        // Struct values
        public const string NonFiniteNumber = "UTOS-V001";

        // Expressions — the static rules of api/docs/template-expressions.md. These parse the
        // text of a condition or a {{ }} template against the language's grammar; they never
        // evaluate it. The grammar is an allow-list of syntax-tree node types, so the codes name
        // what was refused and UTOS-E099 covers everything the list does not mention.
        public const string ExpressionLoop = "UTOS-E001";
        public const string ExpressionFunction = "UTOS-E002";
        public const string ExpressionClass = "UTOS-E003";
        public const string ExpressionStatement = "UTOS-E004";
        public const string ExpressionVar = "UTOS-E010";
        public const string ExpressionArrayHole = "UTOS-E012";
        public const string ExpressionAccessor = "UTOS-E020";
        public const string ExpressionProto = "UTOS-E021";
        public const string ExpressionThis = "UTOS-E030";
        public const string ExpressionAsync = "UTOS-E031";
        public const string ExpressionModule = "UTOS-E032";
        public const string ExpressionSequence = "UTOS-E035";
        public const string ExpressionNew = "UTOS-E040";
        public const string ExpressionForbiddenCall = "UTOS-E041";
        public const string ExpressionUnaryOperator = "UTOS-E050";
        public const string ExpressionBinaryOperator = "UTOS-E051";
        // UTOS-E052 (compound assignment operators) is RETIRED in 0.0.16: every assignment
        // operator is in the language, alongside the bitwise and shift operators it existed to
        // refuse the assignment forms of. Burned, not reused.
        public const string ExpressionSyntaxError = "UTOS-E060";
        public const string ExpressionDelimitedCondition = "UTOS-E061";
        public const string ExpressionUnclosed = "UTOS-E062";
        public const string ExpressionNoValue = "UTOS-E063";
        public const string ExpressionUnknownNode = "UTOS-E099";

#pragma warning restore CS1591
    }
}
