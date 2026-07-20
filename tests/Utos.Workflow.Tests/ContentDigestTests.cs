using System;
using System.Text.RegularExpressions;
using Google.Protobuf.WellKnownTypes;
using Utos.Workflow.V1;
using Xunit;
using Xunit.Abstractions;

// The bare type name `Workflow` collides with the `Utos.Workflow` namespace inside this
// test namespace, so reference the message type through an alias.
using WorkflowMessage = Utos.Workflow.V1.Workflow;

namespace Utos.Workflow.Tests;

public class ContentDigestTests
{
    private readonly ITestOutputHelper _output;

    public ContentDigestTests(ITestOutputHelper output) => _output = output;

    // The canonical JSON of the worked example in api/docs/canonical-bundle-digest.md, JCS-minified
    // (no insignificant whitespace, object keys sorted at every level, array order preserved).
    // This locks the serialization to the spec independently of the hash.
    private const string ExpectedWorkedExampleCanonicalJson =
        "{\"entryPoint\":\"acme/greet:v1\",\"workflows\":{\"acme/greet:v1\":{" +
        "\"apiVersion\":\"utos.io/v1\",\"kind\":\"Workflow\"," +
        "\"metadata\":{\"name\":\"greet\",\"namespace\":\"acme\",\"version\":\"v1\"}," +
        "\"spec\":{\"activities\":{" +
        "\"done\":{\"timer\":{\"duration\":\"5s\"}}," +
        "\"start\":{\"http\":{\"method\":\"GET\",\"url\":\"https://api.example.com\"}," +
        "\"onSuccess\":[" +
        "{\"condition\":\"{{ output.ok }}\",\"transition\":{\"name\":\"done\"}}," +
        "{\"transition\":{\"name\":\"retry\"}}" +
        "]}}}}}}";

    // Builds the spec's worked-example bundle. `activitiesReversed` inserts activities in the
    // opposite (done, start) order to prove map-key sorting is insertion-order independent.
    private static WorkflowBundle WorkedExample(bool activitiesReversed = false)
    {
        var start = new WorkflowActivity
        {
            Http = new HttpActivityConfig { Method = "GET", Url = "https://api.example.com" },
            OnSuccess =
            {
                new TransitionRule
                {
                    Condition = "{{ output.ok }}",
                    Transition = new TransitionTarget { Name = "done" },
                },
                new TransitionRule { Transition = new TransitionTarget { Name = "retry" } },
            },
        };

        var done = new WorkflowActivity
        {
            Timer = new TimerActivityConfig { Duration = Duration.FromTimeSpan(TimeSpan.FromSeconds(5)) },
        };

        var spec = new WorkflowSpec();
        if (activitiesReversed)
        {
            spec.Activities.Add("done", done);
            spec.Activities.Add("start", start);
        }
        else
        {
            spec.Activities.Add("start", start);
            spec.Activities.Add("done", done);
        }

        var workflow = new WorkflowMessage
        {
            ApiVersion = "utos.io/v1",
            Kind = "Workflow",
            Metadata = new WorkflowMetadata { Name = "greet", Namespace = "acme", Version = "v1" },
            Spec = spec,
        };

        return new WorkflowBundle
        {
            EntryPoint = "acme/greet:v1",
            Workflows = { { "acme/greet:v1", workflow } },
        };
    }

    [Fact]
    public void CanonicalJson_matches_spec_worked_example()
    {
        Assert.Equal(ExpectedWorkedExampleCanonicalJson, ContentDigest.CanonicalJson(WorkedExample()));
    }

    [Fact]
    public void Digest_has_sha256_lowerhex_format()
    {
        string digest = WorkedExample().ComputeContentDigest();
        Assert.Matches(new Regex("^sha256:[0-9a-f]{64}$"), digest);
    }

    [Fact]
    public void Digest_is_deterministic_and_independent_of_map_insertion_order()
    {
        string a = ContentDigest.Compute(WorkedExample(activitiesReversed: false));
        string b = ContentDigest.Compute(WorkedExample(activitiesReversed: true));
        Assert.Equal(a, b);
    }

    [Fact]
    public void Verify_matches_computed_digest()
    {
        var bundle = WorkedExample();
        string digest = ContentDigest.Compute(bundle);
        Assert.True(ContentDigest.Verify(bundle, digest));
        Assert.False(ContentDigest.Verify(bundle, "sha256:" + new string('0', 64)));
    }

    [Fact]
    public void Struct_doubles_use_ecmascript_number_formatting()
    {
        // A bundle whose activity carries a Struct with number values. RFC 8785 §3.2.2.3
        // formats 1.0 as "1" (no trailing ".0") and 0.5 as "0.5".
        var bundle = BundleWithResultStruct(new Struct
        {
            Fields =
            {
                { "n", Value.ForNumber(1.0) },
                { "frac", Value.ForNumber(0.5) },
            },
        });

        string json = ContentDigest.CanonicalJson(bundle);
        Assert.Contains("\"n\":1", json);
        Assert.DoesNotContain("1.0", json);
        Assert.Contains("\"frac\":0.5", json);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFinite_struct_numbers_are_rejected(double value)
    {
        var bundle = BundleWithResultStruct(new Struct { Fields = { { "bad", Value.ForNumber(value) } } });
        Assert.Throws<ArgumentException>(() => ContentDigest.Compute(bundle));
    }

    [Fact]
    public void Null_bundle_throws()
    {
        Assert.Throws<ArgumentNullException>(() => ContentDigest.Compute(null!));
    }

    // PROVISIONAL digest of the spec's worked-example bundle, produced by THIS implementation.
    // It is NOT yet a cross-SDK golden value — the digest spec's "Conformance" section gates
    // finalization on a shared reference implementation + committed golden vectors, both deferred.
    // Until then this pins our own output so an accidental pipeline change is caught, and records
    // the candidate value to hand to the daemon when golden vectors are established.
    private const string ProvisionalWorkedExampleDigest =
        "sha256:a3d13ee07b7b397ed4154470c1515b55f9b19c3bac77645916ed19febedf7474";

    [Fact]
    public void Worked_example_matches_provisional_recorded_digest()
    {
        string digest = WorkedExample().ComputeContentDigest();
        _output.WriteLine("PROVISIONAL worked-example digest: " + digest);
        Assert.Equal(ProvisionalWorkedExampleDigest, digest);
    }

    // A minimal valid bundle whose single activity's on_success rule returns the given Struct,
    // giving a place to exercise Struct/Value serialization and the non-finite guard.
    private static WorkflowBundle BundleWithResultStruct(Struct result)
    {
        var activity = new WorkflowActivity
        {
            Http = new HttpActivityConfig { Method = "GET", Url = "https://example.com" },
            OnSuccess = { new TransitionRule { Result = result } },
        };
        var workflow = new WorkflowMessage
        {
            ApiVersion = "utos.io/v1",
            Kind = "Workflow",
            Metadata = new WorkflowMetadata { Name = "s", Version = "v1" },
            Spec = new WorkflowSpec { Activities = { { "a", activity } } },
        };
        return new WorkflowBundle
        {
            EntryPoint = "s:v1",
            Workflows = { { "s:v1", workflow } },
        };
    }
}
