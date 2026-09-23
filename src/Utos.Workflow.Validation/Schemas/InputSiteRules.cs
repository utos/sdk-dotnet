using System;
using System.Collections.Generic;
using Google.Protobuf.WellKnownTypes;

namespace Utos.Workflows.V1.Validation.Schemas
{
    /// <summary>
    /// <c>UTOS-H013</c> and <c>UTOS-H014</c> — the two schema rules that fire <em>before a run
    /// starts</em>: a <c>transition.input</c>, or the <c>input</c> of anything that starts a
    /// document, must supply every property the target activity's declared input requires and none
    /// it does not declare.
    /// <para>
    /// They compare <strong>property sets, never values</strong>. An input transform's keys are
    /// always literal — only its leaf values may be templates — so "this transform can never
    /// satisfy that activity" is knowable with certainty at load, which is the test every rule in
    /// <c>workflow-validation.md</c> has to pass. Whether a template will produce a string is not
    /// knowable, and is deliberately left to the daemon (<c>UTOS-H103</c>, <c>UTOS-H104</c>).
    /// </para>
    /// <para>
    /// <c>workflow-schemas.md</c> calls the second set an <em>invocation</em> rather than reusing
    /// <em>dispatch</em>, because <c>workflow-validation.md</c> holds a <c>workflow.call</c> apart
    /// from a dispatch and that distinction is worth keeping. An invocation is anything that starts
    /// a document with an input: <c>workflow.call</c>, <c>workflow.spawn</c>, a promise branch, and
    /// an <c>onEmitted</c> rule's <c>handle</c>.
    /// </para>
    /// </summary>
    internal static class InputSiteRules
    {
        internal static void Validate(WorkflowBundle bundle, List<ValidationIssue> issues)
        {
            foreach (string key in Paths.SortedKeys(bundle.Workflows))
            {
                Workflow workflow = bundle.Workflows[key];
                if (workflow == null || workflow.Spec == null) continue;

                string activitiesPath = Paths.Field(Paths.Field(Paths.Key("workflows", key), "spec"), "activities");

                foreach (string name in Paths.SortedKeys(workflow.Spec.Activities))
                {
                    WorkflowActivity activity = workflow.Spec.Activities[name];
                    if (activity == null) continue;

                    ValidateActivity(activity, Paths.Key(activitiesPath, name), workflow, bundle, issues);
                }
            }
        }

        private static void ValidateActivity(WorkflowActivity activity, string path, Workflow workflow,
            WorkflowBundle bundle, List<ValidationIssue> issues)
        {
            for (int i = 0; i < activity.OnSuccess.Count; i++)
                ValidateRule(activity.OnSuccess[i], Paths.Index(Paths.Field(path, "onSuccess"), i),
                    workflow, bundle, issues);

            for (int i = 0; i < activity.OnFailure.Count; i++)
                ValidateRule(activity.OnFailure[i], Paths.Index(Paths.Field(path, "onFailure"), i),
                    workflow, bundle, issues);

            switch (activity.ConfigCase)
            {
                case WorkflowActivity.ConfigOneofCase.Workflow:
                    ValidateInvocation(activity.Workflow.Workflow, activity.Workflow.StartActivity,
                        activity.Workflow.Input, Paths.Field(Paths.Field(path, "workflow"), "input"),
                        bundle, issues);

                    if (activity.Workflow.ModeCase == WorkflowActivityConfig.ModeOneofCase.Call)
                    {
                        CallActivityConfig call = activity.Workflow.Call;
                        string emittedPath = Paths.Field(
                            Paths.Field(Paths.Field(path, "workflow"), "call"), "onEmitted");

                        for (int i = 0; i < call.OnEmitted.Count; i++)
                        {
                            ValidateRule(call.OnEmitted[i], Paths.Index(emittedPath, i),
                                workflow, bundle, issues);
                        }
                    }

                    break;

                case WorkflowActivity.ConfigOneofCase.Promise:
                    string branchesPath = Paths.Field(Paths.Field(path, "promise"), "branches");
                    for (int i = 0; i < activity.Promise.Branches.Count; i++)
                    {
                        PromiseBranch branch = activity.Promise.Branches[i];
                        ValidateInvocation(branch.Workflow, branch.StartActivity, branch.Input,
                            Paths.Field(Paths.Index(branchesPath, i), "input"), bundle, issues);
                    }

                    break;
            }
        }

        private static void ValidateRule(TransitionRule rule, string path, Workflow workflow,
            WorkflowBundle bundle, List<ValidationIssue> issues)
        {
            if (rule == null) return;

            // The effect supplies a document with an input, which is UTOS-H014.
            if (rule.EffectCase == TransitionRule.EffectOneofCase.Workflow
                && rule.Workflow.ModeCase == DispatchEffect.ModeOneofCase.Call)
            {
                HandlerDispatch call = rule.Workflow.Call;
                ValidateInvocation(call.Workflow, call.StartActivity, call.Input,
                    Paths.Field(Paths.Field(Paths.Field(path, "workflow"), "call"), "input"),
                    bundle, issues);
            }

            // The exit supplies an activity in this workflow, which is UTOS-H013.
            if (rule.ExitCase == TransitionRule.ExitOneofCase.Transition)
            {
                ValidateTransition(rule.Transition, Paths.Field(path, "transition"), workflow, issues);
            }
        }

        private static void ValidateTransition(TransitionTarget target, string path, Workflow workflow,
            List<ValidationIssue> issues)
        {
            // An omitted transform passes the source activity's output through unchanged, so there
            // is no key set to compare and nothing is knowable.
            if (target == null || target.Input == null) return;
            if (string.IsNullOrEmpty(target.Name)) return;

            WorkflowActivity destination;
            if (!workflow.Spec.Activities.TryGetValue(target.Name, out destination)) return;

            Compare(target.Input, destination, Paths.Field(path, "input"),
                ValidationCodes.TransitionInputMismatch, "transition", target.Name, issues);
        }

        private static void ValidateInvocation(string workflowKey, string startActivity, Struct input,
            string path, WorkflowBundle bundle, List<ValidationIssue> issues)
        {
            if (input == null || string.IsNullOrEmpty(workflowKey) || string.IsNullOrEmpty(startActivity))
                return;

            Workflow callee;
            if (!bundle.Workflows.TryGetValue(workflowKey, out callee)) return;
            if (callee.Spec == null) return;

            WorkflowActivity destination;
            if (!callee.Spec.Activities.TryGetValue(startActivity, out destination)) return;

            Compare(input, destination, path, ValidationCodes.InvocationInputMismatch,
                "invocation", startActivity, issues);
        }

        /// <summary>
        /// One issue per input site, not per offending property: the rule reports that the
        /// transform cannot satisfy the target, and the message says which properties are at
        /// fault. That matches how every other rule in this validator addresses a construct.
        /// </summary>
        private static void Compare(Struct input, WorkflowActivity destination, string path,
            string code, string kind, string targetName, List<ValidationIssue> issues)
        {
            if (destination.Schema == null || destination.Schema.Input == null) return;

            Struct schema = destination.Schema.Input;

            var declared = new HashSet<string>(StringComparer.Ordinal);
            Value propertiesValue;
            if (schema.Fields.TryGetValue("properties", out propertiesValue)
                && propertiesValue.KindCase == Value.KindOneofCase.StructValue)
            {
                foreach (string name in propertiesValue.StructValue.Fields.Keys) declared.Add(name);
            }

            var missing = new List<string>();
            Value requiredValue;
            if (schema.Fields.TryGetValue("required", out requiredValue)
                && requiredValue.KindCase == Value.KindOneofCase.ListValue)
            {
                foreach (Value entry in requiredValue.ListValue.Values)
                {
                    if (entry.KindCase != Value.KindOneofCase.StringValue) continue;
                    if (!input.Fields.ContainsKey(entry.StringValue)) missing.Add(entry.StringValue);
                }
            }

            // Closed means `unevaluatedProperties: false`. An open object accepts anything, so
            // only the missing-required half applies there.
            bool closed = false;
            Value unevaluated;
            if (schema.Fields.TryGetValue("unevaluatedProperties", out unevaluated)
                && unevaluated.KindCase == Value.KindOneofCase.BoolValue)
            {
                closed = !unevaluated.BoolValue;
            }

            var undeclared = new List<string>();
            if (closed)
            {
                foreach (string name in Paths.SortedKeys(input.Fields))
                {
                    if (!declared.Contains(name)) undeclared.Add(name);
                }
            }

            if (missing.Count == 0 && undeclared.Count == 0) return;

            missing.Sort(StringComparer.Ordinal);

            var message = new System.Text.StringBuilder();
            message.Append("This ").Append(kind).Append(" input cannot satisfy the declared input of '")
                .Append(targetName).Append("': ");

            if (missing.Count > 0)
            {
                message.Append("it does not supply required ")
                    .Append(missing.Count == 1 ? "property " : "properties ")
                    .Append(Quote(missing));
            }

            if (missing.Count > 0 && undeclared.Count > 0) message.Append("; ");

            if (undeclared.Count > 0)
            {
                message.Append("it supplies ")
                    .Append(undeclared.Count == 1 ? "property " : "properties ")
                    .Append(Quote(undeclared))
                    .Append(", which that activity does not declare");
            }

            message.Append('.');

            issues.Add(new ValidationIssue(code, path, message.ToString()));
        }

        private static string Quote(List<string> names)
        {
            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < names.Count; i++)
            {
                if (i > 0) builder.Append(", ");
                builder.Append('\'').Append(names[i]).Append('\'');
            }

            return builder.ToString();
        }
    }
}
