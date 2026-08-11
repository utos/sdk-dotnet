using System;
using System.Collections.Generic;

namespace Utos.Workflows.V1
{
    /// <summary>
    /// The completion modes of a promise (fan-out) activity. Each operates on *started* branches,
    /// i.e. after condition filtering. Rule <c>UTOS-C301</c>.
    /// </summary>
    public static class PromiseModes
    {
        /// <summary>All branches must succeed; fails if any branch fails.</summary>
        public const string All = "all";

        /// <summary>Resolves on the first success; fails if every branch fails.</summary>
        public const string Any = "any";

        /// <summary>Resolves on the first settlement, success or failure, which then propagates.</summary>
        public const string Race = "race";

        /// <summary>Resolves once <c>required_count</c> branches succeed; fails once unreachable.</summary>
        public const string Count = "count";

        private static readonly HashSet<string> Known =
            new HashSet<string>(new[] { All, Any, Race, Count }, StringComparer.Ordinal);

        /// <summary>All recognised modes, in the order they are documented.</summary>
        public static IReadOnlyList<string> Names { get; } = new[] { All, Any, Race, Count };

        /// <summary>True when <paramref name="mode"/> is a recognised mode. Comparison is ordinal.</summary>
        public static bool IsKnown(string mode) => mode != null && Known.Contains(mode);
    }
}
