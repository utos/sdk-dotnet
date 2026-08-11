using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Utos.Workflows.V1
{
    /// <summary>
    /// A semantic version, as MAJOR.MINOR.PATCH with optional prerelease and build metadata.
    /// An instance can only exist for a version that parses, so "is this a valid version" is
    /// answered once, at the boundary, rather than re-derived by every caller.
    /// <para>
    /// Ordering follows semver precedence: numeric core first, then a prerelease ranks below its
    /// release (<c>1.0.0-rc.1 &lt; 1.0.0</c>), comparing dot-separated identifiers with numeric ones
    /// ranking below alphanumeric. Build metadata takes no part in precedence.
    /// </para>
    /// <para>
    /// <c>WorkflowMetadata.version</c> carries this form, without a <c>v</c> prefix — see rule
    /// <c>UTOS-M005</c> in <c>api/docs/workflow-validation.md</c>.
    /// </para>
    /// </summary>
    public sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
    {
        private static readonly Regex Pattern =
            new Regex(
                @"^(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private SemanticVersion(int major, int minor, int patch, string prerelease, string buildMetadata)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
            Prerelease = prerelease;
            BuildMetadata = buildMetadata;
        }

        /// <summary>The MAJOR component.</summary>
        public int Major { get; }

        /// <summary>The MINOR component.</summary>
        public int Minor { get; }

        /// <summary>The PATCH component.</summary>
        public int Patch { get; }

        /// <summary>The prerelease identifiers, without the leading '-'. Null when this is a release.</summary>
        public string Prerelease { get; }

        /// <summary>
        /// Build metadata, without the leading '+'. Preserved so <see cref="ToString"/> round-trips
        /// the authored text, but ignored by both comparison and equality, per semver.
        /// </summary>
        public string BuildMetadata { get; }

        /// <summary>True when this version carries prerelease identifiers.</summary>
        public bool IsPrerelease => Prerelease != null;

        /// <summary>Parses <paramref name="version"/>, throwing if it is not a valid semantic version.</summary>
        /// <exception cref="FormatException"><paramref name="version"/> does not parse.</exception>
        public static SemanticVersion Parse(string version)
        {
            SemanticVersion parsed;
            if (TryParse(version, out parsed)) return parsed;
            throw new FormatException("'" + version + "' is not a valid semantic version.");
        }

        /// <summary>Attempts to parse <paramref name="version"/>.</summary>
        public static bool TryParse(string version, out SemanticVersion parsed)
        {
            parsed = null;

            if (string.IsNullOrEmpty(version)) return false;

            Match match = Pattern.Match(version);
            if (!match.Success) return false;

            // The pattern only guarantees the parts are digits, not that they fit in an int.
            int major, minor, patch;
            if (!int.TryParse(match.Groups[1].Value, out major)
                || !int.TryParse(match.Groups[2].Value, out minor)
                || !int.TryParse(match.Groups[3].Value, out patch))
            {
                return false;
            }

            parsed = new SemanticVersion(
                major,
                minor,
                patch,
                match.Groups[4].Success ? match.Groups[4].Value : null,
                match.Groups[5].Success ? match.Groups[5].Value : null);

            return true;
        }

        /// <summary>Renders the version, round-tripping the authored text including build metadata.</summary>
        public override string ToString()
        {
            string core = Major + "." + Minor + "." + Patch;
            if (Prerelease != null) core += "-" + Prerelease;
            if (BuildMetadata != null) core += "+" + BuildMetadata;
            return core;
        }

        /// <summary>Compares by semver precedence. Build metadata takes no part.</summary>
        public int CompareTo(SemanticVersion other)
        {
            if (other is null) return 1;

            int core = Major.CompareTo(other.Major);
            if (core != 0) return core;

            core = Minor.CompareTo(other.Minor);
            if (core != 0) return core;

            core = Patch.CompareTo(other.Patch);
            if (core != 0) return core;

            return ComparePrerelease(Prerelease, other.Prerelease);
        }

        /// <summary>
        /// Equality is precedence equality, so it agrees with <see cref="CompareTo"/>: two versions
        /// differing only in build metadata are the same version. This matters because the version
        /// forms part of a workflow's identity, which is used as a dictionary key.
        /// </summary>
        public bool Equals(SemanticVersion other) =>
            !(other is null)
            && Major == other.Major
            && Minor == other.Minor
            && Patch == other.Patch
            && string.Equals(Prerelease, other.Prerelease, StringComparison.Ordinal);

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as SemanticVersion);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            // System.HashCode is not available on netstandard2.0; this is the same
            // multiply-and-add mix the BCL uses for tuples.
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + Major;
                hash = hash * 31 + Minor;
                hash = hash * 31 + Patch;
                hash = hash * 31 + (Prerelease != null ? StringComparer.Ordinal.GetHashCode(Prerelease) : 0);
                return hash;
            }
        }

#pragma warning disable CS1591 // Comparison operators are self-explanatory.
        public static bool operator <(SemanticVersion left, SemanticVersion right) =>
            Comparer<SemanticVersion>.Default.Compare(left, right) < 0;

        public static bool operator >(SemanticVersion left, SemanticVersion right) =>
            Comparer<SemanticVersion>.Default.Compare(left, right) > 0;

        public static bool operator <=(SemanticVersion left, SemanticVersion right) =>
            Comparer<SemanticVersion>.Default.Compare(left, right) <= 0;

        public static bool operator >=(SemanticVersion left, SemanticVersion right) =>
            Comparer<SemanticVersion>.Default.Compare(left, right) >= 0;

        public static bool operator ==(SemanticVersion left, SemanticVersion right) =>
            left is null ? right is null : left.Equals(right);

        public static bool operator !=(SemanticVersion left, SemanticVersion right) => !(left == right);
#pragma warning restore CS1591

        private static int ComparePrerelease(string left, string right)
        {
            // A version without a prerelease outranks one with it.
            if (left == null && right == null) return 0;
            if (left == null) return 1;
            if (right == null) return -1;

            string[] leftIds = left.Split('.');
            string[] rightIds = right.Split('.');

            int shared = Math.Min(leftIds.Length, rightIds.Length);
            for (int i = 0; i < shared; i++)
            {
                int comparison = ComparePrereleaseIdentifier(leftIds[i], rightIds[i]);
                if (comparison != 0) return comparison;
            }

            // A larger set of identifiers outranks a smaller one when all preceding are equal.
            return leftIds.Length.CompareTo(rightIds.Length);
        }

        private static int ComparePrereleaseIdentifier(string left, string right)
        {
            int leftNumber, rightNumber;
            bool leftIsNumeric = int.TryParse(left, out leftNumber);
            bool rightIsNumeric = int.TryParse(right, out rightNumber);

            // Numeric identifiers always have lower precedence than alphanumeric ones.
            if (leftIsNumeric && rightIsNumeric) return leftNumber.CompareTo(rightNumber);
            if (leftIsNumeric) return -1;
            if (rightIsNumeric) return 1;

            return string.CompareOrdinal(left, right);
        }
    }
}
