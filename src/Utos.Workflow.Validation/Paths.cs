using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Utos.Workflows.V1.Validation
{
    /// <summary>
    /// Builds the <c>path</c> half of a reported issue, in the notation
    /// <c>api/docs/workflow-validation.md</c> § Reporting fixes: canonical lowerCamelCase field
    /// names, map keys as bracketed quoted strings, repeated fields as bracketed indices.
    /// <para>
    /// <c>path</c> is contract — conformance fixtures assert on it — so it is built in one place
    /// rather than concatenated at each site.
    /// </para>
    /// </summary>
    internal static class Paths
    {
        internal static string Field(string path, string field) =>
            path.Length == 0 ? field : path + "." + field;

        internal static string Index(string path, int index) =>
            path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";

        internal static string Key(string path, string key)
        {
            var builder = new StringBuilder(path.Length + key.Length + 4);
            builder.Append(path).Append("[\"");
            foreach (char c in key)
            {
                if (c == '\\' || c == '"') builder.Append('\\');
                builder.Append(c);
            }

            return builder.Append("\"]").ToString();
        }

        internal static List<string> SortedKeys<TValue>(IDictionary<string, TValue> map)
        {
            var keys = new List<string>(map.Keys);
            keys.Sort(System.StringComparer.Ordinal);
            return keys;
        }
    }
}
