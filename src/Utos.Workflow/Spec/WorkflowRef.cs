using System;
using System.Text.RegularExpressions;

namespace Utos.Workflows.V1
{
    /// <summary>
    /// A possibly-partial reference to a workflow, as a user types it:
    /// <c>[registry/][namespace/]name[:version][@sha256:&lt;hex&gt;]</c>.
    /// <para>
    /// Only the name is required. An omitted version means "latest" and is resolved by whoever
    /// holds the workflows; a digest is an exact-content guard applied *after* that resolution,
    /// never a lookup key of its own.
    /// </para>
    /// <para>
    /// This is the counterpart to <see cref="WorkflowIdentity"/>, which is always complete. Map it
    /// onto <c>utos.daemon.v1.WorkflowReference</c> at the transport boundary — this package
    /// deliberately does not depend on the daemon contract.
    /// </para>
    /// </summary>
    public sealed class WorkflowRef
    {
        private static readonly Regex DigestPattern =
            new Regex(@"^sha256:[0-9a-f]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private WorkflowRef(string name, string version, string ns, string registry, string digest)
        {
            Name = name;
            Version = version;
            Namespace = ns;
            Registry = registry;
            Digest = digest;
        }

        /// <summary>The workflow name. Always present.</summary>
        public string Name { get; }

        /// <summary>The requested version, or null for "latest".</summary>
        public string Version { get; }

        /// <summary>The namespace, or null.</summary>
        public string Namespace { get; }

        /// <summary>The registry host, or null.</summary>
        public string Registry { get; }

        /// <summary>The <c>sha256:&lt;hex&gt;</c> content guard, or null.</summary>
        public string Digest { get; }

        /// <summary>Parses a reference.</summary>
        /// <exception cref="FormatException"><paramref name="reference"/> does not parse.</exception>
        public static WorkflowRef Parse(string reference)
        {
            WorkflowRef parsed;
            if (TryParse(reference, out parsed)) return parsed;
            throw new FormatException("'" + reference + "' is not a valid workflow reference.");
        }

        /// <summary>Attempts to parse a reference.</summary>
        public static bool TryParse(string reference, out WorkflowRef parsed)
        {
            parsed = null;
            if (string.IsNullOrEmpty(reference)) return false;

            string rest = reference;
            string digest = null;

            // Digest suffix first — neither a path segment nor a version may contain '@'.
            int at = rest.LastIndexOf('@');
            if (at >= 0)
            {
                digest = rest.Substring(at + 1);
                rest = rest.Substring(0, at);
                if (!DigestPattern.IsMatch(digest)) return false;
                if (rest.Length == 0) return false;
            }

            string version = null;

            // Last ':' rather than first, so a registry carrying a port still splits correctly.
            // A '/' after it means that ':' belonged to the port, not to a version.
            int separator = rest.LastIndexOf(':');
            if (separator >= 0 && rest.IndexOf('/', separator) < 0)
            {
                version = rest.Substring(separator + 1);
                rest = rest.Substring(0, separator);

                SemanticVersion ignored;
                if (!SemanticVersion.TryParse(version, out ignored)) return false;
            }

            string[] segments = rest.Split('/');
            if (segments.Length > 3) return false;
            foreach (string segment in segments)
                if (string.IsNullOrEmpty(segment)) return false;

            string name = segments[segments.Length - 1];
            string ns = segments.Length >= 2 ? segments[segments.Length - 2] : null;
            string registry = segments.Length == 3 ? segments[0] : null;

            parsed = new WorkflowRef(name, version, ns, registry, digest);
            return true;
        }

        /// <summary>Renders the reference, omitting the parts it does not carry.</summary>
        public override string ToString()
        {
            string result = WorkflowIdentity.Join(Registry, Namespace, Name);
            if (Version != null) result += ":" + Version;
            if (Digest != null) result += "@" + Digest;
            return result;
        }
    }
}
