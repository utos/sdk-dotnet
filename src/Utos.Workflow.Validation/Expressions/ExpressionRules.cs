using System;
using System.Collections.Generic;
using System.Globalization;
using Acornima;
using Acornima.Ast;

namespace Utos.Workflows.V1.Validation
{
    /// <summary>
    /// The static expression rules (<c>UTOS-E0##</c>) of <c>api/docs/template-expressions.md</c>,
    /// applied to one field at a time. These parse; they never evaluate.
    /// <para>
    /// A field reports each code at most once — an expression with two loops is one
    /// <c>UTOS-E001</c>, as every other rule here reports its own problem exactly once.
    /// </para>
    /// </summary>
    internal static class ExpressionRules
    {
        private const string Open = "{{";

        /// <summary>A bare expression: the whole string is one program, no delimiters, must end in a value.</summary>
        public static void ValidateCondition(string condition, string path, List<ValidationIssue> issues)
        {
            if (string.IsNullOrEmpty(condition)) return;

            if (condition.IndexOf(Open, StringComparison.Ordinal) >= 0)
            {
                issues.Add(new ValidationIssue(ValidationCodes.ExpressionDelimitedCondition, path,
                    "A condition is a bare expression; write `output.status === 'ready'`, not `{{ ... }}`."));
                return;
            }

            Program program;
            try
            {
                program = ExpressionParser.Parse(condition);
            }
            catch (SyntaxErrorException error)
            {
                issues.Add(new ValidationIssue(ValidationCodes.ExpressionSyntaxError, path,
                    "The condition does not parse: " + error.Message));
                return;
            }

            var codes = new HashSet<string>(StringComparer.Ordinal);
            RequireValue(program, path, issues, codes);
            Grammar(program, path, issues, codes);
        }

        /// <summary>
        /// Any string field that may carry <c>{{ }}</c>: a struct leaf, a URL, a header, a body, a
        /// branch name, a forEach collection. A string with no <c>{{</c> is a literal and passes.
        /// </summary>
        public static void ValidateTemplate(string text, string path, List<ValidationIssue> issues)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf(Open, StringComparison.Ordinal) < 0) return;

            List<TemplateSegment> segments;
            string code, message;
            if (!TemplateSplitter.TryScan(text, out segments, out code, out message))
            {
                issues.Add(new ValidationIssue(code, path, message));
                return;
            }

            var codes = new HashSet<string>(StringComparer.Ordinal);

            if (IsWholeField(segments))
            {
                Program program = FindExpression(segments).Program;
                RequireValue(program, path, issues, codes);
                Grammar(program, path, issues, codes);
                return;
            }

            foreach (TemplateSegment segment in segments)
            {
                if (!segment.IsExpression) continue;

                // Interpolation splices a value into text, so a segment is exactly one expression.
                if (segment.Program.Body.Count != 1 || !(segment.Program.Body[0] is ExpressionStatement))
                {
                    if (codes.Add(ValidationCodes.ExpressionUnclosed))
                        issues.Add(new ValidationIssue(ValidationCodes.ExpressionUnclosed, path,
                            "An interpolated segment must be a single expression followed by '}}'; "
                            + "statements belong in a whole-field `{{ }}`."));
                    continue;
                }

                Grammar(segment.Program, path, issues, codes);
            }
        }

        /// <summary>
        /// True when the text is one whole-field template — a single {{ }} and nothing else
        /// but whitespace. A duration takes a literal or one of these, never interpolation:
        /// half a duration is not a shorter wait.
        /// </summary>
        public static bool IsWholeFieldTemplate(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf(Open, StringComparison.Ordinal) < 0)
                return false;

            List<TemplateSegment> segments;
            string code, message;
            if (!TemplateSplitter.TryScan(text, out segments, out code, out message)) return false;

            return IsWholeField(segments);
        }

        private static bool IsWholeField(List<TemplateSegment> segments)
        {
            int expressions = 0;
            foreach (TemplateSegment segment in segments)
            {
                if (segment.IsExpression) expressions++;
                else if (segment.Text.Trim().Length > 0) return false;
            }

            return expressions == 1;
        }

        private static TemplateSegment FindExpression(List<TemplateSegment> segments)
        {
            foreach (TemplateSegment segment in segments)
                if (segment.IsExpression) return segment;
            return null;
        }

        private static void RequireValue(Program program, string path, List<ValidationIssue> issues, HashSet<string> codes)
        {
            bool endsInValue = program.Body.Count > 0 && program.Body[program.Body.Count - 1] is ExpressionStatement;
            if (!endsInValue && codes.Add(ValidationCodes.ExpressionNoValue))
                issues.Add(new ValidationIssue(ValidationCodes.ExpressionNoValue, path,
                    "The program must end in an expression, which is its value."));
        }

        private static void Grammar(Program program, string path, List<ValidationIssue> issues, HashSet<string> codes)
        {
            foreach (GrammarViolation violation in ExpressionGrammar.Check(program))
            {
                if (!codes.Add(violation.Code)) continue;
                issues.Add(new ValidationIssue(violation.Code, path,
                    violation.Message + " (at " + violation.Line.ToString(CultureInfo.InvariantCulture)
                    + ":" + violation.Column.ToString(CultureInfo.InvariantCulture) + " in the expression)."));
            }
        }
    }
}
