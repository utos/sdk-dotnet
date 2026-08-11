using System;

namespace Utos.Workflows.V1.Validation
{
    /// <summary>
    /// A single rule violation.
    /// <para>
    /// <see cref="Code"/> and <see cref="Path"/> are the contract — conformance fixtures assert
    /// those two and nothing else. <see cref="Message"/> is deliberately *not* contractual, so an
    /// implementation can word errors idiomatically and improve that wording freely.
    /// </para>
    /// </summary>
    public sealed class ValidationIssue
    {
        /// <summary>Creates an issue.</summary>
        public ValidationIssue(string code, string path, string message)
        {
            if (string.IsNullOrEmpty(code)) throw new ArgumentException("Code is required.", nameof(code));

            Code = code;
            Path = path ?? string.Empty;
            Message = message ?? string.Empty;
        }

        /// <summary>The stable rule identifier, e.g. <c>UTOS-M003</c>.</summary>
        public string Code { get; }

        /// <summary>
        /// Where in the bundle the violation is, using canonical lowerCamelCase field names, map
        /// keys as bracketed quoted strings and repeated fields as bracketed indices — e.g.
        /// <c>workflows["acme/greet:1.0.0"].spec.activities["send"].http.url</c>.
        /// </summary>
        public string Path { get; }

        /// <summary>A human-readable explanation. Not contractual.</summary>
        public string Message { get; }

        /// <inheritdoc/>
        public override string ToString() =>
            Path.Length == 0
                ? Code + ": " + Message
                : Code + " at " + Path + ": " + Message;
    }
}
