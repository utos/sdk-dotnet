using System;
using Utos.Workflows.V1;
using Xunit;

namespace Utos.Workflows.Validation.Tests;

public class SemanticVersionTests
{
    [Theory]
    [InlineData("1.0.0")]
    [InlineData("0.0.1")]
    [InlineData("10.20.30")]
    [InlineData("1.0.0-rc.1")]
    [InlineData("1.0.0+build.5")]
    [InlineData("1.0.0-alpha.1+build.5")]
    public void Parses_valid_versions(string version) =>
        Assert.True(SemanticVersion.TryParse(version, out _));

    [Theory]
    [InlineData("")]
    [InlineData("1.0")]
    [InlineData("1")]
    [InlineData("v1.0.0")]          // the 'v' prefix is rejected — rule UTOS-M005
    [InlineData("1.0.0.0")]
    [InlineData("1.0.0-")]
    [InlineData("01.0.0-rc..1")]
    [InlineData("99999999999999999999.0.0")]  // digits, but does not fit in an int
    public void Rejects_invalid_versions(string version) =>
        Assert.False(SemanticVersion.TryParse(version, out _));

    [Fact]
    public void Round_trips_authored_text()
    {
        const string text = "1.2.3-rc.1+build.5";
        Assert.Equal(text, SemanticVersion.Parse(text).ToString());
    }

    [Fact]
    public void Orders_by_semver_precedence()
    {
        var ordered = new[] { "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-rc.1", "1.0.0", "1.0.1", "1.1.0", "2.0.0" };

        for (var i = 1; i < ordered.Length; i++)
        {
            var lower = SemanticVersion.Parse(ordered[i - 1]);
            var higher = SemanticVersion.Parse(ordered[i]);
            Assert.True(lower < higher, $"expected {lower} < {higher}");
        }
    }

    [Fact]
    public void Numeric_prerelease_identifiers_rank_below_alphanumeric() =>
        Assert.True(SemanticVersion.Parse("1.0.0-1") < SemanticVersion.Parse("1.0.0-alpha"));

    [Fact]
    public void Build_metadata_does_not_affect_equality_or_precedence()
    {
        var plain = SemanticVersion.Parse("1.0.0");
        var built = SemanticVersion.Parse("1.0.0+build.5");

        // Precedence equality, so it stays consistent when a version forms part of a dictionary key.
        Assert.Equal(plain, built);
        Assert.Equal(plain.GetHashCode(), built.GetHashCode());
        Assert.Equal(0, plain.CompareTo(built));
    }
}

public class WorkflowIdentityTests
{
    [Theory]
    [InlineData("greet:1.0.0", null, null, "greet")]
    [InlineData("acme/greet:1.0.0", null, "acme", "greet")]
    [InlineData("registry.utos.dev/acme/greet:1.0.0", "registry.utos.dev", "acme", "greet")]
    public void Parses_each_identity_shape(string text, string? registry, string? ns, string name)
    {
        Assert.True(WorkflowIdentity.TryParse(text, out var identity));
        Assert.Equal(registry, identity.Registry);
        Assert.Equal(ns, identity.Namespace);
        Assert.Equal(name, identity.Name);
        Assert.Equal(text, identity.ToString());
    }

    [Fact]
    public void Splits_on_the_last_colon_so_a_registry_port_survives()
    {
        Assert.True(WorkflowIdentity.TryParse("localhost:5000/acme/greet:1.0.0", out var identity));
        Assert.Equal("localhost:5000", identity.Registry);
        Assert.Equal("1.0.0", identity.Version.ToString());
    }

    [Theory]
    [InlineData("greet")]                       // no version
    [InlineData("greet:")]                      // empty version
    [InlineData("greet:notaversion")]
    [InlineData("a/b/c/greet:1.0.0")]           // too many segments
    [InlineData("/greet:1.0.0")]                // empty segment
    [InlineData("localhost:5000/greet")]        // the ':' belongs to the port, leaving no version
    public void Rejects_malformed_identities(string text) =>
        Assert.False(WorkflowIdentity.TryParse(text, out _));

    [Fact]
    public void Formats_metadata_verbatim_without_requiring_validity()
    {
        // Rule UTOS-B005 compares against this, so a malformed name reports only "invalid name"
        // rather than also "key does not match its metadata".
        var metadata = new WorkflowMetadata { Name = "Bad_Name", Version = "v1", Namespace = "acme" };

        Assert.Equal("acme/Bad_Name:v1", WorkflowIdentity.FormatMetadata(metadata));
        Assert.False(WorkflowIdentity.TryFromMetadata(metadata, out _));
    }

    [Fact]
    public void Rejects_a_registry_without_a_namespace() =>
        Assert.Throws<ArgumentException>(() =>
            WorkflowIdentity.Create("greet", SemanticVersion.Parse("1.0.0"), null, "registry.utos.dev"));
}

public class WorkflowRefTests
{
    [Theory]
    [InlineData("greet", null, null, "greet", null, null)]
    [InlineData("greet:1.0.0", null, null, "greet", "1.0.0", null)]
    [InlineData("acme/greet", null, "acme", "greet", null, null)]
    [InlineData("registry.utos.dev/acme/greet:2.1.0", "registry.utos.dev", "acme", "greet", "2.1.0", null)]
    public void Parses_partial_references(string text, string? registry, string? ns, string name,
        string? version, string? digest)
    {
        Assert.True(WorkflowRef.TryParse(text, out var reference));
        Assert.Equal(registry, reference.Registry);
        Assert.Equal(ns, reference.Namespace);
        Assert.Equal(name, reference.Name);
        Assert.Equal(version, reference.Version);
        Assert.Equal(digest, reference.Digest);
        Assert.Equal(text, reference.ToString());
    }

    [Fact]
    public void Parses_a_digest_pin()
    {
        var digest = "sha256:" + new string('a', 64);

        Assert.True(WorkflowRef.TryParse($"acme/greet:1.0.0@{digest}", out var reference));
        Assert.Equal("greet", reference.Name);
        Assert.Equal("1.0.0", reference.Version);
        Assert.Equal(digest, reference.Digest);
    }

    [Fact]
    public void Parses_a_digest_pin_without_a_version()
    {
        var digest = "sha256:" + new string('f', 64);

        Assert.True(WorkflowRef.TryParse($"greet@{digest}", out var reference));
        Assert.Null(reference.Version);
        Assert.Equal(digest, reference.Digest);
    }

    [Theory]
    [InlineData("")]
    [InlineData("greet:notaversion")]
    [InlineData("greet@sha256:short")]
    [InlineData("greet@md5:0123456789abcdef0123456789abcdef")]
    [InlineData("a/b/c/greet")]
    [InlineData("@sha256:0000000000000000000000000000000000000000000000000000000000000000")]
    public void Rejects_malformed_references(string text) =>
        Assert.False(WorkflowRef.TryParse(text, out _));

    [Fact]
    public void Keeps_a_registry_port_out_of_the_version()
    {
        Assert.True(WorkflowRef.TryParse("localhost:5000/acme/greet", out var reference));
        Assert.Equal("localhost:5000", reference.Registry);
        Assert.Null(reference.Version);
    }
}
