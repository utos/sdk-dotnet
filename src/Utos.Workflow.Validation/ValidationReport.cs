using System.Collections.Generic;
using System.Text;

namespace Utos.Workflows.V1.Validation
{
    /// <summary>
    /// The result of validating a <see cref="WorkflowBundle"/>.
    /// <para>
    /// Validation is exhaustive rather than fail-fast: every violation the bundle contains is
    /// reported, so one run surfaces every problem instead of one problem per run.
    /// </para>
    /// </summary>
    public sealed class ValidationReport
    {
        private static readonly ValidationIssue[] None = new ValidationIssue[0];

        internal ValidationReport(IReadOnlyList<ValidationIssue> issues)
        {
            Issues = issues ?? None;
        }

        /// <summary>A report with no violations.</summary>
        public static ValidationReport Valid { get; } = new ValidationReport(None);

        /// <summary>Every violation found, in a stable order.</summary>
        public IReadOnlyList<ValidationIssue> Issues { get; }

        /// <summary>True when no violations were found.</summary>
        public bool IsValid => Issues.Count == 0;

        /// <summary>Renders every issue, one per line.</summary>
        public override string ToString()
        {
            if (Issues.Count == 0) return "valid";

            var builder = new StringBuilder();
            for (int i = 0; i < Issues.Count; i++)
            {
                if (i > 0) builder.Append('\n');
                builder.Append(Issues[i]);
            }

            return builder.ToString();
        }
    }
}
