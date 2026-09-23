using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Utos.Workflows.V1
{
    /// <summary>
    /// The two conversions <c>api/docs/workflow-values.md</c> § JSON defines, and the
    /// well-formedness check of § Well-formed values.
    /// <para>
    /// They live here, once, because every tool needs them identically: the CLI reads
    /// <c>--input '{…}'</c>, a daemon checks what a client scheduled, and a test fixture is JSON.
    /// Two implementations would disagree about numbers, or about what a map holding the key
    /// <c>$blob</c> means — and that second disagreement is the one the typed value exists to
    /// prevent.
    /// </para>
    /// <para>
    /// No reflection anywhere: <see cref="JsonDocument"/> and <see cref="Utf8JsonWriter"/> only,
    /// because the CLI links this assembly into a NativeAOT binary.
    /// </para>
    /// </summary>
    public static class WorkflowValues
    {
        /// <summary>
        /// JSON to a value. **Total, and never produces a blob**: an object holding the key
        /// <c>$blob</c>, or any other key, is a map. A blob reaches a run through a client's own
        /// API — <c>CreateBlob</c> and the handle it returns — and is never spelled in JSON.
        /// </summary>
        /// <exception cref="JsonException">The text is not JSON.</exception>
        public static WorkflowValue FromJson(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));

            using JsonDocument document = JsonDocument.Parse(json);
            return FromJson(document.RootElement);
        }

        /// <summary>JSON to a value, from an element already parsed.</summary>
        public static WorkflowValue FromJson(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    return new WorkflowValue { NullValue = Google.Protobuf.WellKnownTypes.NullValue.NullValue };

                case JsonValueKind.True:
                case JsonValueKind.False:
                    return new WorkflowValue { BoolValue = element.GetBoolean() };

                case JsonValueKind.Number:
                    // The nearest double, which is the one number type a value has. A literal too
                    // large for one is +/- infinity, and a value may not hold that (UTOS-V102), so
                    // it is refused here rather than carried.
                    double number = element.GetDouble();
                    if (double.IsNaN(number) || double.IsInfinity(number))
                        throw new JsonException("A number must be finite; " + element.GetRawText() + " is not.");

                    return new WorkflowValue { NumberValue = number };

                case JsonValueKind.String:
                    return new WorkflowValue { StringValue = element.GetString() };

                case JsonValueKind.Array:
                    var list = new WorkflowList();
                    foreach (JsonElement item in element.EnumerateArray()) list.Values.Add(FromJson(item));
                    return new WorkflowValue { ListValue = list };

                case JsonValueKind.Object:
                    var map = new WorkflowMap();
                    foreach (JsonProperty property in element.EnumerateObject())
                        map.Fields[property.Name] = FromJson(property.Value);
                    return new WorkflowValue { MapValue = map };

                default:
                    throw new JsonException("Unsupported JSON value kind " + element.ValueKind + ".");
            }
        }

        /// <summary>
        /// JSON to the map every carrier's top level is: a run's input, its result, an emitted
        /// value. A document that is not an object at the top is refused here rather than half
        /// converted.
        /// </summary>
        public static WorkflowMap MapFromJson(string json)
        {
            WorkflowValue value = FromJson(json);
            if (value.KindCase != WorkflowValue.KindOneofCase.MapValue)
                throw new JsonException("The top level of a value must be an object.");

            return value.MapValue;
        }

        /// <summary>
        /// A value to JSON — lossless, as long as it holds no blob. Numbers are written in
        /// shortest round-trip form, so a whole number is <c>5</c> rather than <c>5.0</c>, which
        /// is how interpolation renders one.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The value holds a blob. A blob has no plain-JSON form, and inventing one — a key, a
        /// base64 string — would be the very thing the typed value refuses: a type recoverable
        /// only by guessing at content. A tool that must write such a value as text uses the
        /// protobuf JSON mapping instead, which is unambiguous and round-trips.
        /// </exception>
        public static string ToJson(WorkflowValue value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));

            var buffer = new System.IO.MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                Write(value, writer, "");
            }

            return Encoding.UTF8.GetString(buffer.ToArray());
        }

        /// <summary>A map to JSON, as <see cref="ToJson(WorkflowValue)"/>.</summary>
        public static string ToJson(WorkflowMap map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            return ToJson(new WorkflowValue { MapValue = map });
        }

        /// <summary>True when the value holds no blob, and so has a plain-JSON form.</summary>
        public static bool TryToJson(WorkflowValue value, out string json)
        {
            try
            {
                json = ToJson(value);
                return true;
            }
            catch (InvalidOperationException)
            {
                json = null;
                return false;
            }
        }

        private static void Write(WorkflowValue value, Utf8JsonWriter writer, string pointer)
        {
            switch (value.KindCase)
            {
                case WorkflowValue.KindOneofCase.NullValue:
                    writer.WriteNullValue();
                    break;

                case WorkflowValue.KindOneofCase.BoolValue:
                    writer.WriteBooleanValue(value.BoolValue);
                    break;

                case WorkflowValue.KindOneofCase.NumberValue:
                    writer.WriteNumberValue(value.NumberValue);
                    break;

                case WorkflowValue.KindOneofCase.StringValue:
                    writer.WriteStringValue(value.StringValue);
                    break;

                case WorkflowValue.KindOneofCase.ListValue:
                    writer.WriteStartArray();
                    for (int i = 0; i < value.ListValue.Values.Count; i++)
                        Write(value.ListValue.Values[i], writer, pointer + "/" + i);
                    writer.WriteEndArray();
                    break;

                case WorkflowValue.KindOneofCase.MapValue:
                    writer.WriteStartObject();
                    foreach (KeyValuePair<string, WorkflowValue> field in value.MapValue.Fields)
                    {
                        writer.WritePropertyName(field.Key);
                        Write(field.Value, writer, pointer + "/" + Escape(field.Key));
                    }

                    writer.WriteEndObject();
                    break;

                case WorkflowValue.KindOneofCase.BlobValue:
                    throw new InvalidOperationException(
                        "The value at '" + (pointer.Length == 0 ? "/" : pointer) + "' is a blob, "
                        + "which has no plain-JSON form. Write the protobuf JSON mapping of the "
                        + "value instead, or render the blob for display only.");

                default:
                    throw new InvalidOperationException(
                        "The value at '" + (pointer.Length == 0 ? "/" : pointer) + "' has no kind "
                        + "set (UTOS-V101).");
            }
        }

        /// <summary>
        /// <c>UTOS-V101</c> and <c>UTOS-V102</c> over a value that came from outside a daemon —
        /// at <c>ScheduleExecution</c>, the run input. Every failure is reported, not the first,
        /// so one refusal names everything wrong with the request.
        /// <para>
        /// Blobs are checked by the daemon, not here: whether a handle may be attached to this run
        /// depends on what the daemon has stored (<c>UTOS-F104</c>). What this does check is the
        /// shape every blob must have whatever a daemon knows, which is the <c>malformed</c>
        /// reason that code reports.
        /// </para>
        /// </summary>
        public static IReadOnlyList<ValueIssue> Validate(WorkflowValue value)
        {
            var issues = new List<ValueIssue>();
            Check(value, "", issues);
            return issues;
        }

        /// <summary>As <see cref="Validate(WorkflowValue)"/>, over a map.</summary>
        public static IReadOnlyList<ValueIssue> Validate(WorkflowMap map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            return Validate(new WorkflowValue { MapValue = map });
        }

        private static void Check(WorkflowValue value, string pointer, List<ValueIssue> issues)
        {
            if (value == null || value.KindCase == WorkflowValue.KindOneofCase.None)
            {
                issues.Add(new ValueIssue(ValueCodes.KindUnset, pointer,
                    "A value must have exactly one kind set."));
                return;
            }

            switch (value.KindCase)
            {
                case WorkflowValue.KindOneofCase.NumberValue:
                    if (double.IsNaN(value.NumberValue) || double.IsInfinity(value.NumberValue))
                    {
                        issues.Add(new ValueIssue(ValueCodes.NumberNotFinite, pointer,
                            "A number must be finite: neither NaN nor an infinity."));
                    }

                    break;

                case WorkflowValue.KindOneofCase.ListValue:
                    for (int i = 0; i < value.ListValue.Values.Count; i++)
                        Check(value.ListValue.Values[i], pointer + "/" + i, issues);
                    break;

                case WorkflowValue.KindOneofCase.MapValue:
                    foreach (KeyValuePair<string, WorkflowValue> field in value.MapValue.Fields)
                        Check(field.Value, pointer + "/" + Escape(field.Key), issues);
                    break;

                case WorkflowValue.KindOneofCase.BlobValue:
                    CheckBlob(value.BlobValue, pointer, issues);
                    break;
            }
        }

        private static void CheckBlob(Blob blob, string pointer, List<ValueIssue> issues)
        {
            // The invariants of binary-data.md § A blob as a value, which hold whatever a daemon
            // has stored: a negative length, or inline bytes that do not agree with the length or
            // sit at an offset, describe no blob at all.
            if (blob.Size < 0)
            {
                issues.Add(new ValueIssue(ValueCodes.BlobMalformed, pointer,
                    "A blob's size cannot be negative."));
            }

            if (blob.Offset < 0)
            {
                issues.Add(new ValueIssue(ValueCodes.BlobMalformed, pointer,
                    "A blob's offset cannot be negative."));
            }

            if (blob.BackingCase == Blob.BackingOneofCase.Data)
            {
                if (blob.Offset != 0)
                {
                    issues.Add(new ValueIssue(ValueCodes.BlobMalformed, pointer,
                        "An inline blob starts at offset 0; it carries its own bytes."));
                }

                if (blob.Size != blob.Data.Length)
                {
                    issues.Add(new ValueIssue(ValueCodes.BlobMalformed, pointer,
                        "An inline blob's size must equal the length of its bytes."));
                }
            }
            else if (blob.BackingCase == Blob.BackingOneofCase.None)
            {
                issues.Add(new ValueIssue(ValueCodes.BlobMalformed, pointer,
                    "A blob must carry either inline bytes or the id of a stored object."));
            }
        }

        // RFC 6901: a JSON Pointer escapes ~ and / in a member name.
        private static string Escape(string key) =>
            key.Replace("~", "~0").Replace("/", "~1");
    }

    /// <summary>The codes <c>workflow-values.md</c> § Well-formed values defines.</summary>
    public static class ValueCodes
    {
        /// <summary>A value with no kind set at all.</summary>
        public const string KindUnset = "UTOS-V101";

        /// <summary>A number that is NaN or an infinity, neither of which JSON can carry.</summary>
        public const string NumberNotFinite = "UTOS-V102";

        /// <summary>
        /// A blob whose own invariants do not hold. Reported by a daemon as <c>UTOS-F104</c> with
        /// the reason <c>malformed</c>; kept distinct here because this check needs nothing but
        /// the value, while the rest of F104 needs what the daemon has stored.
        /// </summary>
        public const string BlobMalformed = "UTOS-F104";
    }

    /// <summary>One thing wrong with a value, and where.</summary>
    public sealed class ValueIssue
    {
        public ValueIssue(string code, string pointer, string message)
        {
            Code = code;
            Pointer = string.IsNullOrEmpty(pointer) ? "" : pointer;
            Message = message;
        }

        /// <summary>The stable code; the contract, where the message deliberately is not.</summary>
        public string Code { get; }

        /// <summary>A JSON Pointer into the value, <c>""</c> for the value itself.</summary>
        public string Pointer { get; }

        public string Message { get; }

        public override string ToString() =>
            Code + " at '" + (Pointer.Length == 0 ? "/" : Pointer) + "': " + Message;
    }
}
