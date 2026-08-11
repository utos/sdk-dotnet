using System;

namespace Utos.Workflows.V1
{
    /// <summary>
    /// The canonical identity of a workflow — <c>[registry/][namespace/]name:version</c> — derived
    /// from its <see cref="WorkflowMetadata"/>. This is the key under which a workflow appears in
    /// <see cref="WorkflowBundle.Workflows"/>.
    /// <para>
    /// An instance always carries a valid version, so identity is the *resolved* answer. A
    /// possibly-partial request — no version, or a digest pin — is a <see cref="WorkflowRef"/>.
    /// </para>
    /// </summary>
    public sealed class WorkflowIdentity : IEquatable<WorkflowIdentity>
    {
        private WorkflowIdentity(string name, SemanticVersion version, string ns, string registry)
        {
            Name = name;
            Version = version;
            Namespace = ns;
            Registry = registry;
        }

        /// <summary>The workflow name.</summary>
        public string Name { get; }

        /// <summary>The workflow version.</summary>
        public SemanticVersion Version { get; }

        /// <summary>The namespace, or null for a local/unpublished workflow.</summary>
        public string Namespace { get; }

        /// <summary>The registry host, or null for a local/unpublished workflow.</summary>
        public string Registry { get; }

        /// <summary>The identity minus its version — e.g. <c>acme/send-email</c>.</summary>
        public string NameKey => Join(Registry, Namespace, Name);

        /// <summary>
        /// Builds an identity, rejecting parts that would not round-trip through
        /// <see cref="ToString"/>.
        /// </summary>
        /// <exception cref="ArgumentException">A part contains a reserved separator.</exception>
        public static WorkflowIdentity Create(string name, SemanticVersion version, string ns = null,
            string registry = null)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Name is required.", nameof(name));
            if (version == null) throw new ArgumentNullException(nameof(version));

            // Empty and absent must not produce two identities that format the same but compare
            // differently, so optional parts normalise to null.
            ns = Blank(ns) ? null : ns;
            registry = Blank(registry) ? null : registry;

            if (name.IndexOf('/') >= 0 || name.IndexOf(':') >= 0)
                throw new ArgumentException("Name may not contain '/' or ':'.", nameof(name));
            if (ns != null && (ns.IndexOf('/') >= 0 || ns.IndexOf(':') >= 0))
                throw new ArgumentException("Namespace may not contain '/' or ':'.", nameof(ns));
            if (registry != null && registry.IndexOf('/') >= 0)
                throw new ArgumentException("Registry may not contain '/'.", nameof(registry));
            if (registry != null && ns == null)
                throw new ArgumentException("A registry requires a namespace.", nameof(registry));

            return new WorkflowIdentity(name, version, ns, registry);
        }

        /// <summary>Derives the identity of <paramref name="metadata"/>.</summary>
        /// <exception cref="FormatException">The metadata does not describe a valid identity.</exception>
        public static WorkflowIdentity FromMetadata(WorkflowMetadata metadata)
        {
            WorkflowIdentity identity;
            if (TryFromMetadata(metadata, out identity)) return identity;
            throw new FormatException("Workflow metadata does not describe a valid canonical identity.");
        }

        /// <summary>Attempts to derive the identity of <paramref name="metadata"/>.</summary>
        public static bool TryFromMetadata(WorkflowMetadata metadata, out WorkflowIdentity identity)
        {
            identity = null;
            if (metadata == null) return false;

            SemanticVersion version;
            if (!SemanticVersion.TryParse(metadata.Version, out version)) return false;
            if (string.IsNullOrEmpty(metadata.Name)) return false;

            try
            {
                identity = Create(metadata.Name, version, metadata.Namespace, metadata.Registry);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        /// Formats <paramref name="metadata"/> as an identity string **verbatim**, without requiring
        /// the parts to be valid.
        /// <para>
        /// This is what rule <c>UTOS-B005</c> compares a bundle key against. Using the strict
        /// derivation instead would make one malformed name report both "invalid name" and "key
        /// does not match its metadata", when only the first is the actual problem.
        /// </para>
        /// </summary>
        public static string FormatMetadata(WorkflowMetadata metadata)
        {
            if (metadata == null) return string.Empty;
            return Join(metadata.Registry, metadata.Namespace, metadata.Name) + ":" + metadata.Version;
        }

        /// <summary>Parses a canonical identity string.</summary>
        /// <exception cref="FormatException"><paramref name="identity"/> does not parse.</exception>
        public static WorkflowIdentity Parse(string identity)
        {
            WorkflowIdentity parsed;
            if (TryParse(identity, out parsed)) return parsed;
            throw new FormatException("'" + identity + "' is not a valid workflow identity.");
        }

        /// <summary>Attempts to parse a canonical identity string.</summary>
        public static bool TryParse(string identity, out WorkflowIdentity parsed)
        {
            parsed = null;
            if (string.IsNullOrEmpty(identity)) return false;

            // Last ':' rather than first, so a registry carrying a port still splits correctly.
            int separator = identity.LastIndexOf(':');
            if (separator < 0) return false;

            string path = identity.Substring(0, separator);
            string versionText = identity.Substring(separator + 1);

            // A '/' after the separator means that ':' belonged to a registry port, not the version.
            if (versionText.IndexOf('/') >= 0) return false;

            SemanticVersion version;
            if (!SemanticVersion.TryParse(versionText, out version)) return false;

            string[] segments = path.Split('/');
            if (segments.Length > 3) return false;
            foreach (string segment in segments)
                if (string.IsNullOrEmpty(segment)) return false;

            string name = segments[segments.Length - 1];
            string ns = segments.Length >= 2 ? segments[segments.Length - 2] : null;
            string registry = segments.Length == 3 ? segments[0] : null;

            try
            {
                parsed = Create(name, version, ns, registry);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        /// <summary>Renders the canonical <c>[registry/][namespace/]name:version</c> form.</summary>
        public override string ToString() => NameKey + ":" + Version;

        /// <inheritdoc/>
        public bool Equals(WorkflowIdentity other) =>
            !(other is null)
            && string.Equals(Name, other.Name, StringComparison.Ordinal)
            && string.Equals(Namespace, other.Namespace, StringComparison.Ordinal)
            && string.Equals(Registry, other.Registry, StringComparison.Ordinal)
            && Version.Equals(other.Version);

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as WorkflowIdentity);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(Name);
                hash = hash * 31 + (Namespace != null ? StringComparer.Ordinal.GetHashCode(Namespace) : 0);
                hash = hash * 31 + (Registry != null ? StringComparer.Ordinal.GetHashCode(Registry) : 0);
                hash = hash * 31 + Version.GetHashCode();
                return hash;
            }
        }

        internal static string Join(string registry, string ns, string name)
        {
            string result = name ?? string.Empty;
            if (!Blank(ns)) result = ns + "/" + result;
            if (!Blank(registry)) result = registry + "/" + result;
            return result;
        }

        private static bool Blank(string value) => string.IsNullOrEmpty(value);
    }
}
