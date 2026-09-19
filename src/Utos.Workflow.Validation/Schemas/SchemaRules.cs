using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Json.Schema;

namespace Utos.Workflows.V1.Validation.Schemas
{
    /// <summary>
    /// The load-time schema rules of <c>api/docs/workflow-schemas.md</c> — <c>UTOS-H001</c>
    /// through <c>UTOS-H014</c> — over the four slots a workflow may declare: an activity's
    /// <c>schema.input</c>, and <c>spec.output</c>, <c>spec.emits</c> and <c>spec.env</c>.
    /// <para>
    /// Every slot is optional and an absent one is the empty schema, so a bundle built before
    /// schemas existed reports nothing here.
    /// </para>
    /// <para>
    /// These rules <em>inspect</em> a schema. They never evaluate one against run-time data —
    /// that is the daemon's job and takes the <c>UTOS-H1##</c> codes. The one exception is
    /// <c>UTOS-H008</c>, which evaluates a <c>default</c> against the schema that declares it,
    /// so that a default can never be the thing that fails the boundary it was meant to satisfy.
    /// </para>
    /// </summary>
    internal static class SchemaRules
    {
        internal const string Dialect = "https://json-schema.org/draft/2020-12/schema";

        /// <summary>
        /// The asserted formats. An unlisted name is <c>UTOS-H007</c> rather than something
        /// ignored: 2020-12 treats <c>format</c> as an annotation unless a validator opts in, so
        /// a typo'd <c>date-tim</c> would otherwise silently check nothing.
        /// </summary>
        private static readonly HashSet<string> KnownFormats = new HashSet<string>(StringComparer.Ordinal)
        {
            "date-time", "date", "time", "duration", "uuid", "email", "uri", "hostname",
        };

        // Floors from api/docs/workflow-schemas.md § Limits. The spec fixes that each limit
        // exists and a floor every implementation must accept; the actual value is ours.
        private const int MaxDepth = 32;
        private const int MaxProperties = 1024;
        private const int MaxRefs = 128;
        private const int MaxDefs = 128;
        private const int MaxPatternLength = 1024;

        /// <summary>Keywords whose value is a map of name to subschema.</summary>
        private static readonly string[] SubschemaMaps =
            { "properties", "$defs", "patternProperties", "dependentSchemas" };

        /// <summary>Keywords whose value is a single subschema.</summary>
        private static readonly string[] SubschemaFields =
        {
            "items", "additionalProperties", "unevaluatedProperties", "unevaluatedItems",
            "not", "if", "then", "else", "propertyNames", "contains",
        };

        /// <summary>Keywords whose value is an array of subschemas.</summary>
        private static readonly string[] SubschemaArrays =
            { "allOf", "anyOf", "oneOf", "prefixItems" };

        internal enum Slot
        {
            Input,
            Output,
            Emits,
            Env,
        }

        /// <summary>
        /// Validates every schema slot in the bundle: each activity's <c>schema.input</c>, and each
        /// workflow's <c>spec.output</c>, <c>spec.emits</c> and <c>spec.env</c>.
        /// </summary>
        internal static void ValidateBundle(WorkflowBundle bundle, List<ValidationIssue> issues)
        {
            foreach (string key in Paths.SortedKeys(bundle.Workflows))
            {
                Workflow workflow = bundle.Workflows[key];
                if (workflow == null || workflow.Spec == null) continue;

                string specPath = Field(Key("workflows", key), "spec");
                WorkflowSpec spec = workflow.Spec;

                Validate(spec.Output, Field(specPath, "output"), Slot.Output, issues);
                Validate(spec.Emits, Field(specPath, "emits"), Slot.Emits, issues);
                Validate(spec.Env, Field(specPath, "env"), Slot.Env, issues);

                string activitiesPath = Field(specPath, "activities");

                foreach (string name in Paths.SortedKeys(spec.Activities))
                {
                    WorkflowActivity activity = spec.Activities[name];
                    if (activity == null || activity.Schema == null) continue;

                    Validate(activity.Schema.Input,
                        Field(Field(Key(activitiesPath, name), "schema"), "input"), Slot.Input, issues);
                }
            }
        }

        internal static void Validate(Struct schema, string path, Slot slot, List<ValidationIssue> issues)
        {
            if (schema == null) return;

            int before = issues.Count;

            var state = new WalkState();
            WalkObject(schema, path, isRoot: true, depth: 0, slot: slot, root: schema, state: state, issues: issues);

            if (state.Refs > MaxRefs)
            {
                Add(issues, ValidationCodes.SchemaLimitExceeded, path,
                    "Schema declares " + state.Refs + " $refs; the limit is " + MaxRefs + ".");
            }

            ResolveRefs(state, schema, issues);

            // The meta-schema and the default check are a backstop for what the structural rules
            // above do not name, so they run only on a schema those rules accepted. Otherwise a
            // single malformed keyword would report twice — once specifically, once as "does not
            // match the meta-schema" — and a caller suppressing one would still see the other.
            if (issues.Count != before) return;

            ValidateAgainstMetaSchema(schema, path, issues);
            ValidateDefaults(schema, path, issues);
        }

        private sealed class WalkState
        {
            internal int Properties;
            internal int Refs;
            internal bool DepthReported;
            internal bool PropertiesReported;
            internal readonly List<RefSite> RefSites = new List<RefSite>();
        }

        private struct RefSite
        {
            internal string Pointer;
            internal string Path;
        }

        private static void WalkObject(Struct node, string path, bool isRoot, int depth, Slot slot,
            Struct root, WalkState state, List<ValidationIssue> issues)
        {
            if (depth > MaxDepth)
            {
                if (!state.DepthReported)
                {
                    state.DepthReported = true;
                    Add(issues, ValidationCodes.SchemaLimitExceeded, path,
                        "Schema nests deeper than the limit of " + MaxDepth + ".");
                }

                return;
            }

            Value value;

            // UTOS-H004 — the dialect is pinned, so a bundle claiming another one is claiming to be
            // something this spec does not define. A compiler drops $schema entirely.
            if (node.Fields.TryGetValue("$schema", out value))
            {
                string dialect = AsString(value);
                if (!string.Equals(dialect, Dialect, StringComparison.Ordinal))
                {
                    Add(issues, ValidationCodes.SchemaWrongDialect, Field(path, "$schema"),
                        "$schema must be '" + Dialect + "'.");
                }
            }

            // UTOS-H003 — every declared value is an object with named keys, at the top of every
            // slot. Not an array, not a scalar.
            if (isRoot)
            {
                string type = node.Fields.TryGetValue("type", out value) ? AsString(value) : null;
                if (!string.Equals(type, "object", StringComparison.Ordinal))
                {
                    Add(issues, ValidationCodes.SchemaRootNotObject, path,
                        "A schema's top level must declare \"type\": \"object\".");
                }
            }

            if (node.Fields.TryGetValue("$ref", out value))
            {
                state.Refs++;
                ValidateRef(value, Field(path, "$ref"), state, issues);
            }

            if (node.Fields.TryGetValue("format", out value))
            {
                string format = AsString(value);
                if (format != null && !KnownFormats.Contains(format))
                {
                    Add(issues, ValidationCodes.SchemaFormatUnknown, Field(path, "format"),
                        "Unknown format '" + format + "'. Formats are asserted, from a named list: "
                        + string.Join(", ", Sorted(KnownFormats)) + ".");
                }
            }

            if (node.Fields.TryGetValue("pattern", out value))
            {
                ValidatePattern(value, Field(path, "pattern"), issues);
            }

            ValidateDefaultPlacement(node, path, issues);

            if (isRoot && slot == Slot.Env) ValidateEnvProperties(node, path, issues);

            foreach (string keyword in SubschemaMaps)
            {
                if (!node.Fields.TryGetValue(keyword, out value)) continue;
                Struct map = AsStruct(value);
                if (map == null) continue;

                string keywordPath = Field(path, keyword);

                foreach (string name in Sorted(map.Fields.Keys))
                {
                    if (string.Equals(keyword, "properties", StringComparison.Ordinal))
                    {
                        state.Properties++;
                        if (state.Properties > MaxProperties && !state.PropertiesReported)
                        {
                            state.PropertiesReported = true;
                            Add(issues, ValidationCodes.SchemaLimitExceeded, path,
                                "Schema declares more than " + MaxProperties + " properties.");
                        }
                    }

                    WalkSubschema(map.Fields[name], Key(keywordPath, name), depth + 1, slot, root,
                        state, issues);
                }

                if (string.Equals(keyword, "$defs", StringComparison.Ordinal)
                    && map.Fields.Count > MaxDefs)
                {
                    Add(issues, ValidationCodes.SchemaLimitExceeded, keywordPath,
                        "Schema declares " + map.Fields.Count + " $defs; the limit is " + MaxDefs + ".");
                }
            }

            foreach (string keyword in SubschemaFields)
            {
                if (!node.Fields.TryGetValue(keyword, out value)) continue;
                WalkSubschema(value, Field(path, keyword), depth + 1, slot, root, state, issues);
            }

            foreach (string keyword in SubschemaArrays)
            {
                if (!node.Fields.TryGetValue(keyword, out value)) continue;
                ListValue list = value.KindCase == Value.KindOneofCase.ListValue ? value.ListValue : null;
                if (list == null) continue;

                string keywordPath = Field(path, keyword);
                for (int i = 0; i < list.Values.Count; i++)
                    WalkSubschema(list.Values[i], Index(keywordPath, i), depth + 1, slot, root, state, issues);
            }
        }

        /// <summary>
        /// A subschema position. 2020-12 says a schema is an object <em>or a boolean</em>, and the
        /// boolean form stays legal — <c>unevaluatedProperties: false</c> is exactly that, and is
        /// what this spec's own compiler emits. Anything else is not a schema at all
        /// (<c>UTOS-H001</c>), which a <c>Struct</c> will carry happily and nothing else objects to.
        /// </summary>
        private static void WalkSubschema(Value value, string path, int depth, Slot slot,
            Struct root, WalkState state, List<ValidationIssue> issues)
        {
            if (value == null) return;

            switch (value.KindCase)
            {
                case Value.KindOneofCase.StructValue:
                    WalkObject(value.StructValue, path, isRoot: false, depth: depth, slot: slot,
                        root: root, state: state, issues: issues);
                    break;
                case Value.KindOneofCase.BoolValue:
                    break;
                default:
                    Add(issues, ValidationCodes.SchemaNotASchema, path,
                        "A schema must be a JSON object or a boolean.");
                    break;
            }
        }

        /// <summary>
        /// <c>UTOS-H005</c> — a <c>$ref</c> reaches this schema's own <c>$defs</c> and the types
        /// the spec publishes, and nothing else. Keeping evaluation offline is the point: a
        /// workflow's meaning must not depend on what some host served at build time, and a
        /// registry outage cannot be allowed to stop one loading.
        /// <para>
        /// The published registry is <em>empty</em> in this version — <c>blob</c> and <c>file</c>
        /// arrive with binary data — so a <c>utos:</c> name is currently unknown too.
        /// </para>
        /// </summary>
        private static void ValidateRef(Value value, string path, WalkState state,
            List<ValidationIssue> issues)
        {
            string reference = AsString(value);
            if (reference == null)
            {
                Add(issues, ValidationCodes.SchemaRefUnknown, path, "$ref must be a string.");
                return;
            }

            if (reference.StartsWith("#/", StringComparison.Ordinal))
            {
                state.RefSites.Add(new RefSite { Pointer = reference, Path = path });
                return;
            }

            Add(issues, ValidationCodes.SchemaRefUnknown, path,
                "$ref '" + reference + "' must be a JSON Pointer into this schema's own $defs. "
                + "The spec publishes no types in this version, and nothing is ever fetched.");
        }

        private static void ValidatePattern(Value value, string path, List<ValidationIssue> issues)
        {
            string pattern = AsString(value);
            if (pattern == null)
            {
                Add(issues, ValidationCodes.SchemaPatternInvalid, path, "pattern must be a string.");
                return;
            }

            if (pattern.Length > MaxPatternLength)
            {
                Add(issues, ValidationCodes.SchemaLimitExceeded, path,
                    "pattern is longer than the limit of " + MaxPatternLength + " characters.");
                return;
            }

            try
            {
                // Construction is the parse. An unparseable pattern checks nothing, and would
                // otherwise fail at a boundary rather than at load.
                new Regex(pattern, RegexOptions.CultureInvariant);
            }
            catch (ArgumentException e)
            {
                Add(issues, ValidationCodes.SchemaPatternInvalid, path,
                    "pattern is not a valid regular expression: " + e.Message);
            }
        }

        /// <summary>
        /// <c>UTOS-H009</c> — a <c>default</c> on a required property can never be reached, since
        /// the value is always supplied. Rejecting it beats guessing which half the author meant.
        /// </summary>
        private static void ValidateDefaultPlacement(Struct node, string path, List<ValidationIssue> issues)
        {
            Value requiredValue;
            if (!node.Fields.TryGetValue("required", out requiredValue)) return;
            if (requiredValue.KindCase != Value.KindOneofCase.ListValue) return;

            Value propertiesValue;
            if (!node.Fields.TryGetValue("properties", out propertiesValue)) return;
            Struct properties = AsStruct(propertiesValue);
            if (properties == null) return;

            var required = new List<string>();
            foreach (Value entry in requiredValue.ListValue.Values)
            {
                string name = AsString(entry);
                if (name != null) required.Add(name);
            }

            required.Sort(StringComparer.Ordinal);

            foreach (string name in required)
            {
                Value schemaValue;
                if (!properties.Fields.TryGetValue(name, out schemaValue)) continue;
                Struct propertySchema = AsStruct(schemaValue);
                if (propertySchema == null || !propertySchema.Fields.ContainsKey("default")) continue;

                Add(issues, ValidationCodes.SchemaDefaultOnRequired,
                    Field(Key(Field(path, "properties"), name), "default"),
                    "A default may only be declared on a property that is not required; '" + name
                    + "' is always supplied, so its default could never be reached.");
            }
        }

        /// <summary>
        /// <c>UTOS-H011</c> — <c>ScheduleExecutionRequest.env</c> is <c>map&lt;string, string&gt;</c>,
        /// so a declared variable of any other type describes a run that cannot exist.
        /// </summary>
        private static void ValidateEnvProperties(Struct node, string path, List<ValidationIssue> issues)
        {
            Value propertiesValue;
            if (!node.Fields.TryGetValue("properties", out propertiesValue)) return;
            Struct properties = AsStruct(propertiesValue);
            if (properties == null) return;

            string propertiesPath = Field(path, "properties");

            foreach (string name in Sorted(properties.Fields.Keys))
            {
                Struct property = AsStruct(properties.Fields[name]);
                if (property == null) continue;

                Value typeValue;
                string type = property.Fields.TryGetValue("type", out typeValue) ? AsString(typeValue) : null;

                if (!string.Equals(type, "string", StringComparison.Ordinal))
                {
                    Add(issues, ValidationCodes.SchemaEnvNotString, Key(propertiesPath, name),
                        "Every property of spec.env must be of type 'string'; the run's environment "
                        + "is a map of string to string.");
                }
            }
        }

        /// <summary>
        /// <c>UTOS-H006</c> — every <c>$ref</c> must resolve, and a chain of them must terminate.
        /// <para>
        /// Only a chain of bare <c>$ref</c> indirection is a cycle. A schema that reaches itself
        /// <em>through</em> an instance-consuming keyword — <c>properties</c>, <c>items</c> — is an
        /// ordinary recursive schema describing a tree, and terminates on the data. Refusing those
        /// would refuse the reason <c>$defs</c> exists.
        /// </para>
        /// </summary>
        private static void ResolveRefs(WalkState state, Struct root, List<ValidationIssue> issues)
        {
            if (state.RefSites.Count == 0) return;

            var cyclic = new HashSet<string>(StringComparer.Ordinal);

            state.RefSites.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));

            foreach (RefSite site in state.RefSites)
            {
                if (cyclic.Contains(site.Pointer)) continue;

                var seen = new HashSet<string>(StringComparer.Ordinal);
                string pointer = site.Pointer;

                while (true)
                {
                    if (!seen.Add(pointer))
                    {
                        foreach (string visited in seen) cyclic.Add(visited);
                        Add(issues, ValidationCodes.SchemaRefUnresolved, site.Path,
                            "$ref '" + site.Pointer + "' is part of a reference cycle that consumes "
                            + "no instance, so evaluating it would not terminate.");
                        break;
                    }

                    Struct target = Resolve(root, pointer);
                    if (target == null)
                    {
                        Add(issues, ValidationCodes.SchemaRefUnresolved, site.Path,
                            "$ref '" + pointer + "' does not resolve within this schema.");
                        break;
                    }

                    // Follow only a bare $ref. Anything else terminates the chain.
                    Value next;
                    if (!target.Fields.TryGetValue("$ref", out next)) break;

                    string nextPointer = AsString(next);
                    if (nextPointer == null || !nextPointer.StartsWith("#/", StringComparison.Ordinal)) break;

                    pointer = nextPointer;
                }
            }
        }

        /// <summary>Resolves a <c>#/a/b</c> JSON Pointer against the schema root.</summary>
        private static Struct Resolve(Struct root, string pointer)
        {
            Struct current = root;

            // Skip the leading "#/", then walk the escaped segments RFC 6901 defines.
            string[] segments = pointer.Substring(2).Split('/');

            foreach (string rawSegment in segments)
            {
                if (current == null) return null;

                string segment = rawSegment.Replace("~1", "/").Replace("~0", "~");

                Value value;
                if (!current.Fields.TryGetValue(segment, out value)) return null;

                current = AsStruct(value);
            }

            return current;
        }

        /// <summary>
        /// <c>UTOS-H002</c> — the schema is well-formed 2020-12. A backstop for everything the
        /// structural rules above do not name, such as a keyword carrying the wrong JSON type.
        /// </summary>
        private static void ValidateAgainstMetaSchema(Struct schema, string path, List<ValidationIssue> issues)
        {
            string json = ToJson(schema);
            if (json == null) return;

            try
            {
                using (JsonDocument document = JsonDocument.Parse(json))
                {
                    EvaluationResults results = MetaSchemas.Draft202012.Evaluate(
                        document.RootElement,
                        new EvaluationOptions { OutputFormat = OutputFormat.Hierarchical });

                    if (results.IsValid) return;

                    foreach (string pointer in MostSpecific(results))
                    {
                        Add(issues, ValidationCodes.SchemaMalformed, PointerToPath(path, pointer),
                            "Schema is not valid against the JSON Schema 2020-12 meta-schema.");
                    }
                }
            }
            catch (JsonException)
            {
                Add(issues, ValidationCodes.SchemaMalformed, path, "Schema is not well-formed JSON.");
            }
        }

        /// <summary>
        /// The instance locations a reader can actually act on: the leaves of the <em>invalid</em>
        /// subtree.
        /// <para>
        /// Two things have to be skipped, and a flat output format cannot express either. An
        /// invalid node with invalid children is an aggregate — the meta-schema is a composition,
        /// so every real failure is also reported at `allOf`, at `properties`, and at the root.
        /// And a failing branch of a <em>successful</em> <c>anyOf</c> is not a failure at all: the
        /// meta-schema defines <c>type</c> as "a simple type, or an array of them", so
        /// <c>"type": "object"</c> always fails the array alternative while the <c>anyOf</c>
        /// succeeds. Descending only through invalid parents drops both.
        /// </para>
        /// </summary>
        private static List<string> MostSpecific(EvaluationResults results)
        {
            var leaves = new List<string>();
            CollectLeaves(results, leaves);
            leaves.Sort(StringComparer.Ordinal);
            return leaves;
        }

        private static void CollectLeaves(EvaluationResults node, List<string> leaves)
        {
            if (node.IsValid) return;

            bool hasInvalidChild = false;
            if (node.Details != null)
            {
                foreach (EvaluationResults child in node.Details)
                {
                    if (child.IsValid) continue;
                    hasInvalidChild = true;
                    CollectLeaves(child, leaves);
                }
            }

            if (hasInvalidChild) return;

            string pointer = node.InstanceLocation.ToString();
            if (!leaves.Contains(pointer)) leaves.Add(pointer);
        }

        /// <summary>
        /// Turns a JSON Pointer into the schema document into the bundle-path notation
        /// <c>workflow-validation.md</c> § Reporting fixes, so a meta-schema failure names the
        /// keyword a reader has to fix rather than the slot that contains it.
        /// <para>
        /// A segment directly beneath one of the subschema-map keywords is a map key and is
        /// bracketed; an all-digit segment beneath a subschema-array keyword is an index. Anything
        /// else is an ordinary field.
        /// </para>
        /// </summary>
        private static string PointerToPath(string slotPath, string pointer)
        {
            if (string.IsNullOrEmpty(pointer) || pointer == "/") return slotPath;

            string[] segments = pointer.TrimStart('/').Split('/');
            string result = slotPath;
            string previous = null;

            foreach (string rawSegment in segments)
            {
                string segment = rawSegment.Replace("~1", "/").Replace("~0", "~");

                if (previous != null && Array.IndexOf(SubschemaMaps, previous) >= 0)
                    result = Key(result, segment);
                else if (previous != null && Array.IndexOf(SubschemaArrays, previous) >= 0 && IsDigits(segment))
                    result = result + "[" + segment + "]";
                else
                    result = Field(result, segment);

                previous = segment;
            }

            return result;
        }

        private static bool IsDigits(string text)
        {
            if (text.Length == 0) return false;
            foreach (char c in text)
            {
                if (c < '0' || c > '9') return false;
            }

            return true;
        }

        /// <summary>
        /// <c>UTOS-H008</c> — a <c>default</c> validates against the schema that declares it, so a
        /// default can never be the thing that fails the boundary it was meant to satisfy. The
        /// only rule in this file that evaluates a schema rather than inspecting one.
        /// </summary>
        private static void ValidateDefaults(Struct schema, string path, List<ValidationIssue> issues)
        {
            CheckDefaults(schema, path, issues);
        }

        private static void CheckDefaults(Struct node, string path, List<ValidationIssue> issues)
        {
            Value propertiesValue;
            if (node.Fields.TryGetValue("properties", out propertiesValue))
            {
                Struct properties = AsStruct(propertiesValue);
                if (properties != null)
                {
                    string propertiesPath = Field(path, "properties");

                    foreach (string name in Sorted(properties.Fields.Keys))
                    {
                        Struct property = AsStruct(properties.Fields[name]);
                        if (property == null) continue;

                        string propertyPath = Key(propertiesPath, name);

                        Value defaultValue;
                        if (property.Fields.TryGetValue("default", out defaultValue))
                            CheckDefault(property, defaultValue, propertyPath, issues);

                        CheckDefaults(property, propertyPath, issues);
                    }
                }
            }

            Value itemsValue;
            if (node.Fields.TryGetValue("items", out itemsValue))
            {
                Struct items = AsStruct(itemsValue);
                if (items != null) CheckDefaults(items, Field(path, "items"), issues);
            }
        }

        private static void CheckDefault(Struct property, Value defaultValue, string propertyPath,
            List<ValidationIssue> issues)
        {
            // Evaluate the default against the property's own schema, minus the `default` keyword
            // itself, which is an annotation and would not affect the outcome either way.
            string schemaJson = ToJson(property);
            string valueJson = ToJson(defaultValue);
            if (schemaJson == null || valueJson == null) return;

            try
            {
                // FromText, never JsonSerializer.Deserialize<JsonSchema>: the latter is the
                // reflective overload and raises IL2026/IL3050, which the NativeAOT CLI that links
                // this assembly cannot carry.
                JsonSchema schema = JsonSchema.FromText(schemaJson);

                using (JsonDocument document = JsonDocument.Parse(valueJson))
                {
                    EvaluationResults results = schema.Evaluate(
                        document.RootElement,
                        new EvaluationOptions
                        {
                            OutputFormat = OutputFormat.Flag,
                            RequireFormatValidation = true,
                        });

                    if (!results.IsValid)
                    {
                        Add(issues, ValidationCodes.SchemaDefaultInvalid, Field(propertyPath, "default"),
                            "The declared default does not validate against the schema that declares it.");
                    }
                }
            }
            catch (JsonException)
            {
                // A schema that will not parse is UTOS-H002's business, and this method only runs
                // when that rule already passed.
            }
        }

        private static string ToJson(IMessage message)
        {
            try
            {
                return JsonFormatter.Default.Format(message);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private static string AsString(Value value) =>
            value != null && value.KindCase == Value.KindOneofCase.StringValue ? value.StringValue : null;

        private static Struct AsStruct(Value value) =>
            value != null && value.KindCase == Value.KindOneofCase.StructValue ? value.StructValue : null;

        private static List<string> Sorted(IEnumerable<string> names)
        {
            var list = new List<string>(names);
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        private static void Add(List<ValidationIssue> issues, string code, string path, string message)
            => issues.Add(new ValidationIssue(code, path, message));

        private static string Field(string path, string field) => Paths.Field(path, field);

        private static string Index(string path, int index) => Paths.Index(path, index);

        private static string Key(string path, string key) => Paths.Key(path, key);
    }
}
