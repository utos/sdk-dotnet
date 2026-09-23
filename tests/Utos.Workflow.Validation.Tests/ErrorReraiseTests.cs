using System.Linq;
using Google.Protobuf.WellKnownTypes;
using Utos.Workflows.V1;
using Utos.Workflows.V1.Validation;
using Xunit;

namespace Utos.Workflows.Validation.Tests;

/// <summary>
/// UTOS-T005 and its one exception: an <c>error</c> with no fields in an <c>onFailure</c> rule
/// re-raises the failure being handled. The vendored conformance corpus (<c>utos/api</c> 0.0.17)
/// carries the same cases as bundles; these pin the rule directly, including the <c>details</c>-only case
/// the corpus does not have.
/// </summary>
public class ErrorReraiseTests
{
    private const string Key = "greet:1.0.0";

    [Fact]
    public void An_empty_error_on_onFailure_is_a_reraise()
    {
        var bundle = Bundle(activity => activity.OnFailure.Add(new TransitionRule { Error = new WorkflowError() }));

        Assert.True(WorkflowBundleValidator.Validate(bundle).IsValid);
    }

    [Fact]
    public void An_empty_error_on_onSuccess_has_nothing_to_reraise()
    {
        var bundle = Bundle(activity => activity.OnSuccess.Add(new TransitionRule { Error = new WorkflowError() }));

        AssertT005(bundle, $"workflows[\"{Key}\"].spec.activities[\"say-hello\"].onSuccess[0].error.code");
    }

    [Fact]
    public void An_empty_error_on_onEmitted_has_nothing_to_reraise()
    {
        var bundle = Bundle(activity =>
        {
            activity.Http = null;
            activity.Workflow = new WorkflowActivityConfig
            {
                Workflow = Key,
                StartActivity = "say-hello",
                Call = new CallActivityConfig { OnEmitted = { new TransitionRule { Error = new WorkflowError() } } },
            };
        });

        AssertT005(bundle, $"workflows[\"{Key}\"].spec.activities[\"say-hello\"].workflow.call.onEmitted[0].error.code");
    }

    [Theory]
    [InlineData("message")]
    [InlineData("details")]
    public void A_partly_written_error_on_onFailure_is_not_a_reraise(string field)
    {
        var error = field == "message"
            ? new WorkflowError { Message = "hello failed: {{ error.message }}" }
            : new WorkflowError { Details = new Struct { Fields = { ["status"] = Value.ForString("{{ response.status }}") } } };
        var bundle = Bundle(activity => activity.OnFailure.Add(new TransitionRule { Error = error }));

        AssertT005(bundle, $"workflows[\"{Key}\"].spec.activities[\"say-hello\"].onFailure[0].error.code");
    }

    private static void AssertT005(WorkflowBundle bundle, string path)
    {
        var issues = WorkflowBundleValidator.Validate(bundle).Issues.Select(i => (i.Code, i.Path)).ToList();
        Assert.Equal([(ValidationCodes.ErrorCodeRequired, path)], issues);
    }

    private static WorkflowBundle Bundle(System.Action<WorkflowActivity> configure)
    {
        var activity = new WorkflowActivity
        {
            Http = new HttpActivityConfig { Method = "GET", Url = "https://api.example.com/hello" },
        };
        configure(activity);

        return new WorkflowBundle
        {
            EntryPoint = Key,
            Workflows =
            {
                [Key] = new Workflow
                {
                    ApiVersion = WorkflowDocument.ApiVersion,
                    Kind = WorkflowDocument.Kind,
                    Metadata = new WorkflowMetadata { Name = "greet", Version = "1.0.0" },
                    Spec = new WorkflowSpec { Activities = { ["say-hello"] = activity } },
                },
            },
        };
    }
}
