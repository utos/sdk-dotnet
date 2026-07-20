using System;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using Org.Webpki.JsonCanonicalizer;

namespace Utos.Workflow.V1
{
    /// <summary>
    /// Computes the canonical content digest of a <see cref="WorkflowBundle"/>, as defined by the
    /// Utos specification (<c>api/docs/canonical-bundle-digest.md</c>):
    /// <code>digest = "sha256:" + lowerhex( sha256( JCS( proto3json( WorkflowBundle ) ) ) )</code>
    /// <para>
    /// The bundle is serialized with the proto3 JSON mapping, canonicalized per RFC 8785 (JSON
    /// Canonicalization Scheme), then SHA-256 hashed. The result is a stable, cross-SDK content
    /// identity — the value carried by <c>WorkflowReference.digest</c> — independent of the mutable
    /// <c>name:version</c> key.
    /// </para>
    /// </summary>
    public static class ContentDigest
    {
        /// <summary>Digest algorithm prefix. Only SHA-256 is defined by the current spec.</summary>
        public const string Sha256Prefix = "sha256:";

        // Proto3 JSON with default settings implements the spec's pinned rules 1-6:
        //   * lowerCamelCase json_name (rule 1),
        //   * omit implicit-presence scalars at default + unset optionals + empty maps/repeated,
        //     keep explicitly-set optionals (rule 2),
        //   * maps -> JSON objects, order-significant lists -> JSON arrays (rules 3-4),
        //   * Struct/Value handling (rule 5), Duration -> string form e.g. "5s" (rule 6).
        // Do NOT enable FormatDefaultValues or PreserveProtoFieldNames — both would break the
        // digest. The bundle graph contains no google.protobuf.Any, so no TypeRegistry is needed.
        private static readonly JsonFormatter Formatter =
            new JsonFormatter(JsonFormatter.Settings.Default);

        /// <summary>
        /// Returns the canonical JSON (proto3 JSON mapping followed by RFC 8785 / JCS) whose UTF-8
        /// bytes are hashed to produce the digest. Exposed so conformance tests can assert the
        /// serialization independently of the hash.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="bundle"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// The bundle contains a non-finite number (NaN or ±Infinity) in a <c>Struct</c> value,
        /// which is forbidden by the spec (rule 5).
        /// </exception>
        public static string CanonicalJson(WorkflowBundle bundle)
        {
            if (bundle == null) throw new ArgumentNullException(nameof(bundle));
            RejectNonFiniteNumbers(bundle);
            string protoJson = Formatter.Format(bundle);
            return new JsonCanonicalizer(protoJson).GetEncodedString();
        }

        /// <summary>
        /// Computes the content digest of <paramref name="bundle"/> as
        /// <c>"sha256:" + lowercase-hex(SHA-256(canonical-JSON UTF-8 bytes))</c>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="bundle"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// The bundle contains a non-finite number (NaN or ±Infinity) in a <c>Struct</c> value.
        /// </exception>
        public static string Compute(WorkflowBundle bundle)
        {
            if (bundle == null) throw new ArgumentNullException(nameof(bundle));
            RejectNonFiniteNumbers(bundle);
            string protoJson = Formatter.Format(bundle);
            byte[] canonicalUtf8 = new JsonCanonicalizer(protoJson).GetEncodedUTF8();
            byte[] hash;
            using (var sha = SHA256.Create())
            {
                hash = sha.ComputeHash(canonicalUtf8);
            }
            return Sha256Prefix + ToLowerHex(hash);
        }

        /// <summary>
        /// Returns <c>true</c> if <paramref name="expected"/> equals the computed digest of
        /// <paramref name="bundle"/>. Comparison is ordinal; digests are lowercase hex.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="bundle"/> or <paramref name="expected"/> is null.
        /// </exception>
        public static bool Verify(WorkflowBundle bundle, string expected)
        {
            if (expected == null) throw new ArgumentNullException(nameof(expected));
            return string.Equals(Compute(bundle), expected, StringComparison.Ordinal);
        }

        private static readonly char[] HexDigits = "0123456789abcdef".ToCharArray();

        private static string ToLowerHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                sb.Append(HexDigits[b >> 4]);
                sb.Append(HexDigits[b & 0xF]);
            }
            return sb.ToString();
        }

        // Spec rule 5: NaN and ±Infinity are not representable as JSON numbers and are forbidden in
        // bundle Struct values. We reject them explicitly and deterministically rather than relying
        // on the JSON formatter's behavior. The only reachable doubles in the bundle graph live in
        // google.protobuf.Value (Struct/ListValue); everything else is string/bool/int32/Duration.
        private static void RejectNonFiniteNumbers(IMessage message)
        {
            switch (message)
            {
                case Google.Protobuf.WellKnownTypes.Value value:
                    switch (value.KindCase)
                    {
                        case Google.Protobuf.WellKnownTypes.Value.KindOneofCase.NumberValue:
                            double d = value.NumberValue;
                            if (double.IsNaN(d) || double.IsInfinity(d))
                            {
                                throw new ArgumentException(
                                    "WorkflowBundle contains a non-finite number (NaN or Infinity) " +
                                    "in a Struct value, which is forbidden by the canonical bundle " +
                                    "digest spec (rule 5).");
                            }
                            return;
                        case Google.Protobuf.WellKnownTypes.Value.KindOneofCase.StructValue:
                            RejectNonFiniteNumbers(value.StructValue);
                            return;
                        case Google.Protobuf.WellKnownTypes.Value.KindOneofCase.ListValue:
                            foreach (var item in value.ListValue.Values)
                                RejectNonFiniteNumbers(item);
                            return;
                        default:
                            return;
                    }
                case Google.Protobuf.WellKnownTypes.Struct structValue:
                    foreach (var v in structValue.Fields.Values)
                        RejectNonFiniteNumbers(v);
                    return;
            }

            foreach (var field in message.Descriptor.Fields.InFieldNumberOrder())
            {
                if (field.FieldType != Google.Protobuf.Reflection.FieldType.Message) continue;

                object value = field.Accessor.GetValue(message);
                if (value == null) continue;

                if (field.IsMap)
                {
                    foreach (var entryValue in ((System.Collections.IDictionary)value).Values)
                        if (entryValue is IMessage nested) RejectNonFiniteNumbers(nested);
                }
                else if (field.IsRepeated)
                {
                    foreach (var item in (System.Collections.IEnumerable)value)
                        if (item is IMessage nested) RejectNonFiniteNumbers(nested);
                }
                else if (value is IMessage singular)
                {
                    RejectNonFiniteNumbers(singular);
                }
            }
        }
    }
}
