using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Google.Protobuf.WellKnownTypes;
using Utos.Workflows.V1.Validation.Schemas;

namespace Utos.Workflows.V1.Validation
{
    /// <summary>
    /// Validates a <see cref="WorkflowBundle"/> against the rules in
    /// <c>api/docs/workflow-validation.md</c>.
    /// <para>
    /// The rules are structural and referential: they check shape and that names resolve. They
    /// parse template expressions against the grammar of <c>api/docs/template-expressions.md</c>
    /// but never evaluate one, reach the network, or reason about what a workflow will do at run
    /// time.
    /// </para>
    /// </summary>
    public static class WorkflowBundleValidator
    {
        private const int MaxNameLength = 63;
        private const int MaxDescriptionLength = 500;

        private static readonly Regex WorkflowNamePattern =
            new Regex(@"^[a-z0-9]([a-z0-9-]*[a-z0-9])?$",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex ActivityNamePattern =
            new Regex(@"^[a-zA-Z0-9_-]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>Validates <paramref name="bundle"/>, reporting every violation it contains.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="bundle"/> is null.</exception>
        public static ValidationReport Validate(WorkflowBundle bundle)
        {
            if (bundle == null) throw new ArgumentNullException(nameof(bundle));

            var issues = new List<ValidationIssue>();

            if (string.IsNullOrEmpty(bundle.EntryPoint))
            {
                Add(issues, ValidationCodes.EntryPointRequired, "entryPoint",
                    "Bundle entryPoint is required.");
            }

            if (bundle.Workflows.Count == 0)
            {
                Add(issues, ValidationCodes.WorkflowsRequired, "workflows",
                    "Bundle must contain at least one workflow.");
            }

            if (!string.IsNullOrEmpty(bundle.EntryPoint)
                && bundle.Workflows.Count > 0
                && !bundle.Workflows.ContainsKey(bundle.EntryPoint))
            {
                Add(issues, ValidationCodes.EntryPointNotFound, "entryPoint",
                    "Entry point '" + bundle.EntryPoint + "' is not a key of workflows. Available: "
                    + string.Join(", ", SortedKeys(bundle.Workflows)) + ".");
            }

            // MapField enumeration order is unspecified; sorting keeps reports reproducible.
            foreach (string key in SortedKeys(bundle.Workflows))
            {
                string path = Key("workflows", key);
                Workflow workflow = bundle.Workflows[key];

                if (key.Length == 0)
                {
                    Add(issues, ValidationCodes.WorkflowKeyRequired, path,
                        "Workflow key cannot be empty.");
                }

                ValidateWorkflow(workflow, key, path, bundle, issues);
            }

            // The schema rules run as their own pass. They are defined over whole schema documents
            // and, for UTOS-H013/H014, over a transform and the activity it targets — neither of
            // which fits the activity-at-a-time walk above, and both of which need the bundle.
            SchemaRules.ValidateBundle(bundle, issues);
            InputSiteRules.Validate(bundle, issues);

            return new ValidationReport(issues);
        }

        private static void ValidateWorkflow(Workflow workflow, string key, string path,
            WorkflowBundle bundle, List<ValidationIssue> issues)
        {
            if (workflow == null) return;

            if (!string.Equals(workflow.ApiVersion, WorkflowDocument.ApiVersion, StringComparison.Ordinal))
            {
                Add(issues, ValidationCodes.ApiVersionInvalid, Field(path, "apiVersion"),
                    "apiVersion must be '" + WorkflowDocument.ApiVersion + "', not '"
                    + workflow.ApiVersion + "'.");
            }

            if (!string.Equals(workflow.Kind, WorkflowDocument.Kind, StringComparison.Ordinal))
            {
                Add(issues, ValidationCodes.KindInvalid, Field(path, "kind"),
                    "kind must be '" + WorkflowDocument.Kind + "', not '" + workflow.Kind + "'.");
            }

            if (workflow.Metadata == null)
            {
                Add(issues, ValidationCodes.MetadataRequired, Field(path, "metadata"),
                    "metadata is required.");
            }
            else
            {
                ValidateMetadata(workflow.Metadata, Field(path, "metadata"), issues);

                // Compared against the identity formatted from metadata verbatim, so a malformed
                // name reports only that — not also "the key does not match its metadata".
                if (key.Length > 0)
                {
                    string derived = WorkflowIdentity.FormatMetadata(workflow.Metadata);
                    if (!string.Equals(key, derived, StringComparison.Ordinal))
                    {
                        Add(issues, ValidationCodes.WorkflowKeyMismatch, path,
                            "Workflow key '" + key + "' does not match the canonical identity derived "
                            + "from its metadata ('" + derived + "').");
                    }
                }
            }

            if (workflow.Spec == null)
            {
                Add(issues, ValidationCodes.SpecRequired, Field(path, "spec"), "spec is required.");
                return;
            }

            string specPath = Field(path, "spec");

            if (workflow.Spec.Dependencies.Count > 0)
            {
                Add(issues, ValidationCodes.DependenciesNotEmptied, Field(specPath, "dependencies"),
                    "spec.dependencies must be empty in a built bundle; aliases are resolved to "
                    + "canonical identities at build time.");
            }

            if (workflow.Spec.Activities.Count == 0)
            {
                Add(issues, ValidationCodes.ActivitiesRequired, Field(specPath, "activities"),
                    "A workflow must contain at least one activity.");
                return;
            }

            var activityNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in workflow.Spec.Activities.Keys) activityNames.Add(name);

            string activitiesPath = Field(specPath, "activities");

            foreach (string name in SortedKeys(workflow.Spec.Activities))
            {
                ValidateActivity(workflow.Spec.Activities[name], name, Key(activitiesPath, name),
                    activityNames, bundle, issues);
            }
        }

        private static void ValidateMetadata(WorkflowMetadata metadata, string path,
            List<ValidationIssue> issues)
        {
            // One violation per field: the most specific applicable rule wins, so a 70-character
            // uppercase name reports "too long" rather than every rule the name happens to break.
            if (string.IsNullOrEmpty(metadata.Name))
            {
                Add(issues, ValidationCodes.NameRequired, Field(path, "name"),
                    "Workflow name is required.");
            }
            else if (metadata.Name.Length > MaxNameLength)
            {
                Add(issues, ValidationCodes.NameTooLong, Field(path, "name"),
                    "Workflow name is too long (maximum " + MaxNameLength + " characters).");
            }
            else if (!WorkflowNamePattern.IsMatch(metadata.Name))
            {
                Add(issues, ValidationCodes.NameInvalid, Field(path, "name"),
                    DescribeNameProblem(metadata.Name));
            }

            if (string.IsNullOrEmpty(metadata.Version))
            {
                Add(issues, ValidationCodes.VersionRequired, Field(path, "version"),
                    "Workflow version is required.");
            }
            else
            {
                SemanticVersion parsed;
                if (!SemanticVersion.TryParse(metadata.Version, out parsed))
                {
                    Add(issues, ValidationCodes.VersionInvalid, Field(path, "version"),
                        DescribeVersionProblem(metadata.Version));
                }
            }

            if (metadata.HasDescription && metadata.Description.Length > MaxDescriptionLength)
            {
                Add(issues, ValidationCodes.DescriptionTooLong, Field(path, "description"),
                    "Description is too long (maximum " + MaxDescriptionLength + " characters).");
            }

            if (metadata.HasNamespace
                && (metadata.Namespace.Length == 0
                    || metadata.Namespace.IndexOf('/') >= 0
                    || metadata.Namespace.IndexOf(':') >= 0))
            {
                Add(issues, ValidationCodes.NamespaceInvalid, Field(path, "namespace"),
                    "Namespace must be non-empty and contain no '/' or ':', so that canonical "
                    + "identity parses back unambiguously.");
            }

            if (metadata.HasRegistry
                && (metadata.Registry.Length == 0 || metadata.Registry.IndexOf('/') >= 0))
            {
                Add(issues, ValidationCodes.RegistryInvalid, Field(path, "registry"),
                    "Registry must be non-empty and contain no '/'.");
            }

            if (metadata.HasRegistry && metadata.Registry.Length > 0 && !metadata.HasNamespace)
            {
                Add(issues, ValidationCodes.RegistryRequiresNamespace, Field(path, "registry"),
                    "A registry requires a namespace; '<registry>/<name>:<version>' cannot be told "
                    + "apart from '<namespace>/<name>:<version>'.");
            }
        }

        private static void ValidateActivity(WorkflowActivity activity, string name, string path,
            HashSet<string> activityNames, WorkflowBundle bundle, List<ValidationIssue> issues)
        {
            ValidateActivityName(name, path, issues);

            if (activity == null) return;

            switch (activity.ConfigCase)
            {
                case WorkflowActivity.ConfigOneofCase.Http:
                    ValidateHttp(activity.Http, Field(path, "http"), issues);
                    break;
                case WorkflowActivity.ConfigOneofCase.Timer:
                    ValidateTimer(activity.Timer, Field(path, "timer"), issues);
                    break;
                case WorkflowActivity.ConfigOneofCase.Promise:
                    ValidatePromise(activity.Promise, Field(path, "promise"), bundle, issues);
                    break;
                case WorkflowActivity.ConfigOneofCase.Workflow:
                    ValidateSubWorkflow(activity.Workflow, Field(path, "workflow"), activityNames,
                        bundle, issues);
                    break;
                default:
                    // The oneof cannot be set twice — protobuf enforces that structurally — but it
                    // can easily be omitted, and an activity that does nothing is malformed.
                    Add(issues, ValidationCodes.ActivityConfigRequired, path,
                        "Activity must have exactly one configuration set (http, workflow, promise "
                        + "or timer).");
                    break;
            }

            for (int i = 0; i < activity.OnSuccess.Count; i++)
                ValidateRule(activity.OnSuccess[i], Index(Field(path, "onSuccess"), i),
                    activityNames, bundle, issues, failureInScope: false, exitRequired: true);

            for (int i = 0; i < activity.OnFailure.Count; i++)
                ValidateRule(activity.OnFailure[i], Index(Field(path, "onFailure"), i),
                    activityNames, bundle, issues, failureInScope: true, exitRequired: true);
        }

        private static void ValidateActivityName(string name, string path, List<ValidationIssue> issues)
        {
            if (string.IsNullOrEmpty(name) || name.Trim().Length != name.Length || name.Trim().Length == 0)
            {
                Add(issues, ValidationCodes.ActivityNameRequired, path,
                    "Activity name is required and cannot have leading or trailing whitespace.");
            }
            else if (name.Length > MaxNameLength)
            {
                Add(issues, ValidationCodes.ActivityNameTooLong, path,
                    "Activity name is too long (maximum " + MaxNameLength + " characters).");
            }
            else if (!ActivityNamePattern.IsMatch(name))
            {
                Add(issues, ValidationCodes.ActivityNameInvalid, path,
                    "Activity name may contain only letters, digits, underscores and hyphens.");
            }
            else if (char.IsDigit(name[0]) || name[0] == '-' || name[0] == '_')
            {
                Add(issues, ValidationCodes.ActivityNameBadStart, path,
                    "Activity name cannot start with a digit, hyphen or underscore.");
            }
            else if (name[name.Length - 1] == '-' || name[name.Length - 1] == '_')
            {
                Add(issues, ValidationCodes.ActivityNameBadEnd, path,
                    "Activity name cannot end with a hyphen or underscore.");
            }
        }

        /// <summary>
        /// UTOS-T001, over the one rule type every list carries since spec 0.20.0: an optional
        /// condition, at most one <b>effect</b> — what happens — and at most one <b>exit</b> —
        /// where the run goes next.
        /// <para>
        /// "At most one" needs no check: each is a proto <c>oneof</c>, so a second one cannot be
        /// expressed. What is left is a rule with <em>neither</em>, which matched a condition and
        /// then did nothing — a dead end rather than a skip — and, where the rules end the
        /// activity, a rule with an effect and no exit, which would strand the execution.
        /// </para>
        /// <para>
        /// <paramref name="exitRequired"/> is what separates the lists: <c>onSuccess</c> and
        /// <c>onFailure</c> run when the activity is over, so the run has to go somewhere, while
        /// an <c>onEmitted</c> rule runs once per value and an exit is what <em>stops</em> the
        /// consuming loop.
        /// </para>
        /// </summary>
        private static void ValidateRule(TransitionRule rule, string path,
            HashSet<string> activityNames, WorkflowBundle bundle, List<ValidationIssue> issues,
            bool failureInScope, bool exitRequired)
        {
            if (rule == null) return;

            if (rule.HasCondition)
                ExpressionRules.ValidateCondition(rule.Condition, Field(path, "condition"), issues);

            switch (rule.EffectCase)
            {
                case TransitionRule.EffectOneofCase.Emit:
                    // An empty struct is a value: a rule may emit {} deliberately.
                    if (rule.Emit != null) ValidateStruct(rule.Emit, Field(path, "emit"), issues);
                    break;
                case TransitionRule.EffectOneofCase.Workflow:
                    ValidateDispatchEffect(rule.Workflow, Field(path, "workflow"), bundle, issues);
                    break;
            }

            switch (rule.ExitCase)
            {
                case TransitionRule.ExitOneofCase.Transition:
                    ValidateTarget(rule.Transition, Field(path, "transition"), activityNames, issues);
                    break;
                case TransitionRule.ExitOneofCase.Result:
                    // An empty struct is a complete exit: it ends the path with no value.
                    if (rule.Result != null) ValidateStruct(rule.Result, Field(path, "result"), issues);
                    break;
                case TransitionRule.ExitOneofCase.Error:
                    ValidateError(rule.Error, Field(path, "error"), issues, failureInScope);
                    break;
                default:
                    if (rule.EffectCase == TransitionRule.EffectOneofCase.None)
                    {
                        Add(issues, ValidationCodes.RuleEffectOrExitRequired, path,
                            "A rule must carry an effect (emit, workflow.call) or an exit "
                            + "(transition, result, error); one that carries neither matches and "
                            + "then does nothing.");
                    }
                    else if (exitRequired)
                    {
                        Add(issues, ValidationCodes.RuleEffectOrExitRequired, path,
                            "A rule in this list needs an exit (transition, result, error): the "
                            + "activity is over, so the run has to go somewhere.");
                    }
                    break;
            }
        }

        /// <summary>
        /// A rule's dispatch effect: run a document, in the mode the key named. The modes are a
        /// nested oneof, so <c>workflow.call</c> in a document is <c>effect</c> -> <c>workflow</c>
        /// -> <c>mode</c> -> <c>call</c>, and an unset mode is the same defect an activity with no
        /// configuration has.
        /// </summary>
        private static void ValidateDispatchEffect(DispatchEffect effect, string path,
            WorkflowBundle bundle, List<ValidationIssue> issues)
        {
            if (effect == null) return;

            switch (effect.ModeCase)
            {
                case DispatchEffect.ModeOneofCase.Call:
                    ValidateDispatch(effect.Call.Workflow, effect.Call.StartActivity, effect.Call.Input,
                        Field(path, "call"), bundle, issues);
                    break;
                default:
                    Add(issues, ValidationCodes.ActivityConfigRequired, path,
                        "A dispatch effect must name its mode: workflow.call.");
                    break;
            }
        }

        /// <summary>
        /// UTOS-T005. An <c>error</c> action is the <c>WorkflowError</c> the run will report,
        /// authored: <c>code</c> is the literal a consumer matches on and is required; the
        /// <c>message</c> text and the <c>details</c> struct are templates.
        /// <para>
        /// The exception is the re-raise: an <c>error</c> with no fields at all, where there is a
        /// failure in scope to re-raise — an <c>onFailure</c> rule. After a success, or when an
        /// <c>onEmitted</c> rule fires on a value, <c>error</c> is null and an empty action has
        /// nothing to re-raise. A partly written one on <c>onFailure</c> (a message and no code)
        /// is still T005: it is a mistake, not a re-raise.
        /// </para>
        /// </summary>
        private static void ValidateError(WorkflowError error, string path, List<ValidationIssue> issues,
            bool failureInScope)
        {
            if (error == null) return;

            bool isReraise = string.IsNullOrEmpty(error.Code) && string.IsNullOrEmpty(error.Message)
                && error.Details == null;
            if (isReraise && failureInScope) return;

            if (string.IsNullOrEmpty(error.Code))
            {
                Add(issues, ValidationCodes.ErrorCodeRequired, Field(path, "code"),
                    "An error action requires a code; it is what on_failure rules and consumers "
                    + "match on."
                    + (failureInScope ? " Only an error with no fields at all re-raises the failure being handled." : ""));
            }

            ExpressionRules.ValidateTemplate(error.Message, Field(path, "message"), issues);
            if (error.Details != null) ValidateStruct(error.Details, Field(path, "details"), issues);
        }

        private static void ValidateTarget(TransitionTarget target, string path,
            HashSet<string> activityNames, List<ValidationIssue> issues)
        {
            if (target == null) return;

            if (string.IsNullOrEmpty(target.Name))
            {
                Add(issues, ValidationCodes.TransitionTargetRequired, Field(path, "name"),
                    "Transition target name is required.");
            }
            else if (!activityNames.Contains(target.Name))
            {
                // A target is always an activity. Until 0.0.15 `end` and `error` were keywords
                // here; a document that still uses them lands on this rule, which is intended —
                // the replacement is a `result` or `error` action, and the message says so.
                Add(issues, ValidationCodes.TransitionTargetUnresolved, Field(path, "name"),
                    "Transition target '" + target.Name + "' is not an activity in this workflow"
                    + (IsFormerKeyword(target.Name)
                        ? "; to end a path use a result action, to fail it use an error action."
                        : "."));
            }

            if (target.Input != null) ValidateStruct(target.Input, Field(path, "input"), issues);
        }

        private static void ValidateHttp(HttpActivityConfig config, string path,
            List<ValidationIssue> issues)
        {
            if (config == null) return;

            if (string.IsNullOrEmpty(config.Url))
            {
                Add(issues, ValidationCodes.HttpUrlRequired, Field(path, "url"), HttpUrlRules.EmptyError + ".");
            }
            else if (!HttpUrlRules.IsValidAuthored(config.Url))
            {
                Add(issues, ValidationCodes.HttpUrlInvalid, Field(path, "url"),
                    HttpUrlRules.GetError(config.Url) + ".");
            }

            if (string.IsNullOrEmpty(config.Method))
            {
                Add(issues, ValidationCodes.HttpMethodRequired, Field(path, "method"),
                    "HTTP method is required.");
            }

            // Text fields: rendered at run time, so any {{ }} in them must be in the language.
            ExpressionRules.ValidateTemplate(config.Url, Field(path, "url"), issues);
            foreach (string header in SortedKeys(config.Headers))
                ExpressionRules.ValidateTemplate(config.Headers[header], Key(Field(path, "headers"), header), issues);
            if (config.HasBody)
                ExpressionRules.ValidateTemplate(config.Body, Field(path, "body"), issues);
        }

        private static void ValidateTimer(TimerActivityConfig config, string path,
            List<ValidationIssue> issues)
        {
            if (config == null) return;

            string duration = config.Duration;
            if (string.IsNullOrEmpty(duration))
            {
                Add(issues, ValidationCodes.TimerDurationRequired, Field(path, "duration"),
                    "Timer activity requires a duration.");
                return;
            }

            // A whole-field template stands in for the literal, and no load-time rule can
            // evaluate it: what it renders to is checked when the activity is entered
            // (UTOS-E106). The same division UTOS-C102 draws for a templated URL.
            if (ExpressionRules.IsWholeFieldTemplate(duration))
            {
                ExpressionRules.ValidateTemplate(duration, Field(path, "duration"), issues);
                return;
            }

            if (!WorkflowDuration.TryParse(duration, out _, out WorkflowDuration.Failure failure))
            {
                if (failure == WorkflowDuration.Failure.NotPositive)
                {
                    // No maximum: a long wait is legitimate. Only a non-positive one is
                    // malformed, and it would otherwise surface mid-run rather than here.
                    Add(issues, ValidationCodes.TimerDurationNotPositive, Field(path, "duration"),
                        "Timer duration must be positive.");
                }
                else
                {
                    Add(issues, ValidationCodes.TimerDurationSyntax, Field(path, "duration"),
                        "Timer duration must be a duration string — 90s, 8h, 1h30m — with whole "
                        + "numbers, units largest first and no spaces.");
                }
            }
        }

        private static void ValidatePromise(PromiseActivityConfig config, string path,
            WorkflowBundle bundle, List<ValidationIssue> issues)
        {
            if (config == null) return;

            // An unknown completion mode is no longer representable — that is what retiring
            // UTOS-C301 in favour of the `completion` oneof bought. An *unset* one is still
            // possible, and is UTOS-A007 like any other omitted configuration.
            if (config.CompletionCase == PromiseActivityConfig.CompletionOneofCase.None)
            {
                Add(issues, ValidationCodes.ActivityConfigRequired, path,
                    "Promise activity must have exactly one completion mode set (all, any, race "
                    + "or count).");
            }
            else if (config.CompletionCase == PromiseActivityConfig.CompletionOneofCase.Count
                     && config.Count.RequiredCount <= 0)
            {
                // requiredCount lives only on PromiseCountConfig now, so this needs no mode guard.
                Add(issues, ValidationCodes.PromiseRequiredCountInvalid,
                    Field(Field(path, "count"), "requiredCount"),
                    "promise.count requires requiredCount greater than zero.");
            }

            if (config.Branches.Count == 0)
            {
                Add(issues, ValidationCodes.PromiseBranchesRequired, Field(path, "branches"),
                    "Promise activity must have at least one branch.");
                return;
            }

            // Rendered branch names must be distinct or the output map loses an entry, but
            // rendering needs the forEach collection and these rules do not evaluate templates.
            // Literal collisions are the half that is knowable here, and the half that can never
            // turn out to be fine.
            HashSet<string> literalBranchNames = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < config.Branches.Count; i++)
            {
                PromiseBranch branch = config.Branches[i];
                string branchPath = Index(Field(path, "branches"), i);
                if (branch == null) continue;

                if (string.IsNullOrEmpty(branch.Name))
                {
                    Add(issues, ValidationCodes.PromiseBranchNameRequired, Field(branchPath, "name"),
                        "Promise branch name is required.");
                }
                else if (!literalBranchNames.Add(branch.Name))
                {
                    Add(issues, ValidationCodes.PromiseBranchNameDuplicate, Field(branchPath, "name"),
                        "Two branches of this promise are named '" + branch.Name
                        + "'; branch names key the promise output map and must be distinct.");
                }

                ValidateDispatch(branch.Workflow, branch.StartActivity, branch.Input,
                    branchPath, bundle, issues);

                ExpressionRules.ValidateTemplate(branch.Name, Field(branchPath, "name"), issues);
                if (branch.HasCondition)
                    ExpressionRules.ValidateCondition(branch.Condition, Field(branchPath, "condition"), issues);

                if (branch.ForEach != null
                    && (string.IsNullOrEmpty(branch.ForEach.Collection)
                        || string.IsNullOrEmpty(branch.ForEach.Alias)))
                {
                    Add(issues, ValidationCodes.PromiseForEachIncomplete, Field(branchPath, "forEach"),
                        "forEach requires both a collection and an alias.");
                }
                else if (branch.ForEach != null)
                {
                    ExpressionRules.ValidateTemplate(branch.ForEach.Collection,
                        Field(Field(branchPath, "forEach"), "collection"), issues);
                }
            }
        }

        private static void ValidateSubWorkflow(WorkflowActivityConfig config, string path,
            HashSet<string> activityNames, WorkflowBundle bundle, List<ValidationIssue> issues)
        {
            if (config == null) return;

            // call vs spawn changes what this activity returns and whether on_failure sees the
            // child's runtime failures, so leaving it unset is as malformed as an activity with no
            // configuration at all.
            if (config.ModeCase == WorkflowActivityConfig.ModeOneofCase.None)
            {
                Add(issues, ValidationCodes.ActivityConfigRequired, path,
                    "Sub-workflow activity must have exactly one mode set (call or spawn).");
            }
            else if (config.ModeCase == WorkflowActivityConfig.ModeOneofCase.Call)
            {
                ValidateOnEmitted(config.Call, Field(Field(path, "call"), "onEmitted"),
                    activityNames, bundle, issues);
            }

            Workflow target = null;

            if (string.IsNullOrEmpty(config.Workflow))
            {
                Add(issues, ValidationCodes.SubWorkflowRequired, Field(path, "workflow"),
                    "Sub-workflow reference is required.");
            }
            else if (!bundle.Workflows.TryGetValue(config.Workflow, out target))
            {
                // A bundle is self-contained by definition: aliases were resolved to canonical
                // identities at build time, so every reference must point at something inside it.
                Add(issues, ValidationCodes.SubWorkflowNotInBundle, Field(path, "workflow"),
                    "Sub-workflow '" + config.Workflow + "' is not a key of workflows.");
            }

            if (string.IsNullOrEmpty(config.StartActivity))
            {
                Add(issues, ValidationCodes.SubWorkflowStartActivityRequired,
                    Field(path, "startActivity"), "Sub-workflow startActivity is required.");
            }
            else if (target != null && target.Spec != null
                     && !target.Spec.Activities.ContainsKey(config.StartActivity))
            {
                Add(issues, ValidationCodes.SubWorkflowStartActivityUnresolved,
                    Field(path, "startActivity"),
                    "Start activity '" + config.StartActivity + "' does not exist in sub-workflow '"
                    + config.Workflow + "'.");
            }

            if (config.Input != null) ValidateStruct(config.Input, Field(path, "input"), issues);
        }

        /// <summary>
        /// The <c>onEmitted</c> list, which since spec 0.20.0 carries the same rule every other
        /// list carries. The only difference is that an exit is optional here: a rule with an
        /// effect and no exit handles the value and comes back for the next, and any exit stops
        /// consuming.
        /// <para>
        /// A value arrived rather than a failure, so there is nothing in scope to re-raise and a
        /// bare <c>error</c> is still UTOS-T005.
        /// </para>
        /// </summary>
        private static void ValidateOnEmitted(CallActivityConfig call, string path,
            HashSet<string> activityNames, WorkflowBundle bundle, List<ValidationIssue> issues)
        {
            if (call == null) return;

            for (int i = 0; i < call.OnEmitted.Count; i++)
            {
                ValidateRule(call.OnEmitted[i], Index(path, i), activityNames, bundle, issues,
                    failureInScope: false, exitRequired: false);
            }
        }

        /// <summary>
        /// UTOS-C501-C503, over the three fields a promise branch and an onEmitted rule share.
        /// <para>
        /// One method rather than one per construct, mirroring the single rule range: a dispatch
        /// means "run this document, starting here" wherever it appears, and a reader who has
        /// learned what UTOS-C502 means has learned it in both places.
        /// </para>
        /// <para>
        /// Deliberately not folded together with the UTOS-C401-C403 checks on a workflow.call,
        /// which read the same but are not the same thing: a call is an activity in the current
        /// flow that waits for a result and whose failure is that activity's failure. A dispatch
        /// has no activity of its own.
        /// </para>
        /// </summary>
        private static void ValidateDispatch(string workflow, string startActivity, Struct input,
            string path, WorkflowBundle bundle, List<ValidationIssue> issues)
        {
            Workflow target = null;

            if (string.IsNullOrEmpty(workflow))
            {
                Add(issues, ValidationCodes.DispatchWorkflowRequired, Field(path, "workflow"),
                    "A dispatch must name the workflow to run.");
            }
            else if (!bundle.Workflows.TryGetValue(workflow, out target))
            {
                // `self` never reaches a bundle — the CLI rewrites it to this document's own
                // canonical identity at build time, and an alias that resolves to nothing is a
                // source-format error (UTOS-S004) caught before a bundle exists.
                Add(issues, ValidationCodes.SubWorkflowNotInBundle, Field(path, "workflow"),
                    "Dispatched workflow '" + workflow + "' is not a key of workflows.");
            }

            if (string.IsNullOrEmpty(startActivity))
            {
                Add(issues, ValidationCodes.DispatchStartActivityRequired,
                    Field(path, "startActivity"),
                    "A dispatch must name the activity to start from.");
            }
            else if (target != null && target.Spec != null
                     && !target.Spec.Activities.ContainsKey(startActivity))
            {
                Add(issues, ValidationCodes.DispatchStartActivityUnresolved,
                    Field(path, "startActivity"),
                    "Start activity '" + startActivity + "' does not exist in workflow '"
                    + workflow + "'.");
            }

            if (input != null) ValidateStruct(input, Field(path, "input"), issues);
        }

        private static void ValidateStruct(Struct value, string path, List<ValidationIssue> issues)
        {
            foreach (string field in SortedKeys(value.Fields))
                ValidateValue(value.Fields[field], Field(path, field), issues);
        }

        private static void ValidateValue(Value value, string path, List<ValidationIssue> issues)
        {
            if (value == null) return;

            switch (value.KindCase)
            {
                case Value.KindOneofCase.NumberValue:
                    if (double.IsNaN(value.NumberValue) || double.IsInfinity(value.NumberValue))
                    {
                        // Neither is representable in JSON, so either would make the bundle
                        // unserializable and its content digest uncomputable.
                        Add(issues, ValidationCodes.NonFiniteNumber, path,
                            "Struct values cannot contain NaN or Infinity.");
                    }

                    break;
                case Value.KindOneofCase.StringValue:
                    // A leaf string may carry {{ }}; one without is a literal and passes.
                    ExpressionRules.ValidateTemplate(value.StringValue, path, issues);
                    break;
                case Value.KindOneofCase.StructValue:
                    ValidateStruct(value.StructValue, path, issues);
                    break;
                case Value.KindOneofCase.ListValue:
                    for (int i = 0; i < value.ListValue.Values.Count; i++)
                        ValidateValue(value.ListValue.Values[i], Index(path, i), issues);
                    break;
            }
        }

        private static string DescribeNameProblem(string name)
        {
            if (name.IndexOf('_') >= 0)
                return "Workflow name cannot contain underscores; use hyphens instead.";
            if (name != name.ToLowerInvariant())
                return "Workflow name must be lowercase.";
            if (name[0] == '-')
                return "Workflow name cannot start with a hyphen.";
            if (name[name.Length - 1] == '-')
                return "Workflow name cannot end with a hyphen.";

            return "Workflow name '" + name
                   + "' may contain only lowercase letters, digits and hyphens.";
        }

        private static string DescribeVersionProblem(string version)
        {
            if (version.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                return "Semantic version must not include a 'v' prefix.";

            string[] parts = version.Split('.');
            if (parts.Length == 2)
                return "Incomplete semantic version (must be MAJOR.MINOR.PATCH).";

            return "'" + version + "' is not a valid semantic version.";
        }

        private static bool IsFormerKeyword(string name) =>
            string.Equals(name, "end", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "error", StringComparison.OrdinalIgnoreCase);

        private static void Add(List<ValidationIssue> issues, string code, string path, string message)
            => issues.Add(new ValidationIssue(code, path, message));

        private static List<string> SortedKeys<TValue>(IDictionary<string, TValue> map)
        {
            var keys = new List<string>(map.Keys);
            keys.Sort(StringComparer.Ordinal);
            return keys;
        }

        private static string Field(string path, string field) =>
            path.Length == 0 ? field : path + "." + field;

        private static string Index(string path, int index) =>
            path + "[" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]";

        private static string Key(string path, string key)
        {
            var builder = new StringBuilder(path.Length + key.Length + 4);
            builder.Append(path).Append("[\"");
            foreach (char c in key)
            {
                if (c == '\\' || c == '"') builder.Append('\\');
                builder.Append(c);
            }

            return builder.Append("\"]").ToString();
        }
    }
}
