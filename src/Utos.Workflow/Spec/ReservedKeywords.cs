using System;
using System.Collections.Generic;

namespace Utos.Workflows.V1
{
    /// <summary>
    /// The terminal keywords a transition may name instead of an activity. Because they are
    /// reserved, an activity may not take either name (rule <c>UTOS-A003</c>) and a transition
    /// target is therefore never ambiguous.
    /// <para>
    /// Matching is case-insensitive: this is a closed two-word vocabulary, and <c>End</c> can only
    /// have been meant as the keyword. Activity *names*, by contrast, resolve ordinally.
    /// </para>
    /// </summary>
    public static class ReservedKeywords
    {
        /// <summary>End this execution path successfully.</summary>
        public const string End = "end";

        /// <summary>End this execution path with an error.</summary>
        public const string Error = "error";

        private static readonly HashSet<string> Keywords =
            new HashSet<string>(new[] { End, Error }, StringComparer.OrdinalIgnoreCase);

        /// <summary>All reserved keywords.</summary>
        public static IEnumerable<string> All => Keywords;

        /// <summary>True when <paramref name="name"/> is a reserved terminal keyword.</summary>
        public static bool IsReserved(string name) => name != null && Keywords.Contains(name);
    }
}
