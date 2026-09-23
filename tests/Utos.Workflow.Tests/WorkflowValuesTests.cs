using System.Linq;
using System.Text.Json;
using Google.Protobuf;
using Utos.Workflows.V1;
using Xunit;

namespace Utos.Workflows.Tests;

/// <summary>
/// The conversions of <c>api/docs/workflow-values.md</c> § JSON, and the well-formedness of
/// § Well-formed values. What is asserted here is what two tools have to agree about.
/// </summary>
public class WorkflowValuesTests
{
    [Fact]
    public void Json_round_trips_through_a_value()
    {
        const string json = """
            {"id":"o-17","count":3,"ratio":2.5,"ok":true,"missing":null,"tags":["a","b"],
             "nested":{"deep":{"n":1}}}
            """;

        var value = WorkflowValues.FromJson(json);
        var back = WorkflowValues.ToJson(value);

        using var expected = JsonDocument.Parse(json);
        using var actual = JsonDocument.Parse(back);
        Assert.Equal(expected.RootElement.GetRawText().Replace(" ", "").Replace("\r", "").Replace("\n", ""),
            actual.RootElement.GetRawText().Replace(" ", ""));
    }

    [Fact]
    public void A_whole_number_is_written_without_a_fraction()
    {
        // The rendering interpolation uses: 5, never 5.0. A value carries one number type, so the
        // only question is how it is written back out.
        var value = WorkflowValues.FromJson("""{"n":5.0,"m":2.5}""");

        Assert.Equal("""{"n":5,"m":2.5}""", WorkflowValues.ToJson(value));
    }

    [Fact]
    public void A_dollar_blob_key_is_data_in_both_directions()
    {
        // The rule the typed value exists to make true: no key means a type. A response, a
        // client's input and a child's result may all contain this key, and it stays a map.
        const string json = """{"meta":{"$blob":{"id":"b_forged"}}}""";

        var value = WorkflowValues.FromJson(json);
        var meta = value.MapValue.Fields["meta"];

        Assert.Equal(WorkflowValue.KindOneofCase.MapValue, meta.KindCase);
        Assert.Equal(WorkflowValue.KindOneofCase.MapValue, meta.MapValue.Fields["$blob"].KindCase);
        Assert.Equal(json, WorkflowValues.ToJson(value));
    }

    [Fact]
    public void Json_never_produces_a_blob()
    {
        // There is no JSON that means "a blob". One reaches a run as a handle a client was given.
        var value = WorkflowValues.FromJson("""{"data":"aGk=","id":"b_1","size":2}""");

        Assert.All(value.MapValue.Fields.Values,
            field => Assert.NotEqual(WorkflowValue.KindOneofCase.BlobValue, field.KindCase));
    }

    [Fact]
    public void A_value_holding_a_blob_has_no_json_form()
    {
        var value = new WorkflowValue
        {
            MapValue = new WorkflowMap
            {
                Fields =
                {
                    ["photo"] = new WorkflowValue
                    {
                        BlobValue = new Blob { Id = "b_01JB8", Size = 10342, MediaType = "image/png" },
                    },
                },
            },
        };

        Assert.False(WorkflowValues.TryToJson(value, out var json));
        Assert.Null(json);

        // The message has to name where, or a caller cannot act on it.
        var error = Assert.Throws<System.InvalidOperationException>(() => WorkflowValues.ToJson(value));
        Assert.Contains("/photo", error.Message);
    }

    [Fact]
    public void A_top_level_that_is_not_an_object_is_refused()
    {
        Assert.Throws<JsonException>(() => WorkflowValues.MapFromJson("[1, 2]"));
        Assert.Throws<JsonException>(() => WorkflowValues.MapFromJson("\"hello\""));
    }

    [Fact]
    public void A_pointer_escapes_the_characters_rfc6901_reserves()
    {
        var value = new WorkflowValue
        {
            MapValue = new WorkflowMap
            {
                Fields = { ["a/b~c"] = new WorkflowValue { NumberValue = double.NaN } },
            },
        };

        var issue = Assert.Single(WorkflowValues.Validate(value));
        Assert.Equal("/a~1b~0c", issue.Pointer);
    }

    [Fact]
    public void Every_failure_is_reported_not_the_first()
    {
        var value = new WorkflowValue
        {
            MapValue = new WorkflowMap
            {
                Fields =
                {
                    ["a"] = new WorkflowValue { NumberValue = double.NaN },
                    ["b"] = new WorkflowValue(),                                  // no kind set
                    ["c"] = new WorkflowValue { NumberValue = double.PositiveInfinity },
                },
            },
        };

        var issues = WorkflowValues.Validate(value);

        Assert.Equal(3, issues.Count);
        Assert.Equal(new[] { "UTOS-V101", "UTOS-V102", "UTOS-V102" },
            issues.Select(i => i.Code).OrderBy(c => c).ToArray());
    }

    [Fact]
    public void A_well_formed_value_reports_nothing()
    {
        var value = WorkflowValues.FromJson("""{"a":1,"b":[true,null,"x"],"c":{"d":{}}}""");

        Assert.Empty(WorkflowValues.Validate(value));
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("1e400")]           // a literal no double can hold: +infinity
    public void A_number_json_cannot_carry_is_refused_at_the_door(string literal)
    {
        Assert.ThrowsAny<JsonException>(() => WorkflowValues.FromJson("{\"n\":" + literal + "}"));
    }

    [Fact]
    public void An_inline_blob_must_agree_with_its_own_bytes()
    {
        var value = new WorkflowValue
        {
            BlobValue = new Blob
            {
                Data = ByteString.CopyFromUtf8("hi"),
                Size = 99,                                   // not the length of the bytes
                Offset = 3,                                  // inline blobs start at 0
                MediaType = "text/plain",
            },
        };

        var issues = WorkflowValues.Validate(value);

        Assert.Equal(2, issues.Count);
        Assert.All(issues, i => Assert.Equal("UTOS-F104", i.Code));
    }

    [Fact]
    public void A_blob_with_no_backing_is_malformed()
    {
        var value = new WorkflowValue { BlobValue = new Blob { Size = 0, MediaType = "" } };

        var issue = Assert.Single(WorkflowValues.Validate(value));
        Assert.Equal("UTOS-F104", issue.Code);
    }

    [Fact]
    public void A_stored_blob_is_well_formed_and_has_no_json_form()
    {
        // The two halves of what a handle is: a value the daemon can resolve, and not JSON.
        var value = new WorkflowValue
        {
            BlobValue = new Blob { Id = "b_01JB8", Offset = 2, Size = 3, MediaType = "text/plain" },
        };

        Assert.Empty(WorkflowValues.Validate(value));
        Assert.False(WorkflowValues.TryToJson(value, out _));
    }
}
