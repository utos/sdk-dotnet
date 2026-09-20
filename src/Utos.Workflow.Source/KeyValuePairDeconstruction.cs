using System.Collections.Generic;

namespace Utos.Workflows.V1.Source
{
    /// <summary>
    /// <c>foreach (var (key, value) in map)</c> over a <see cref="KeyValuePair{TKey,TValue}"/>.
    /// <para>
    /// The framework grew a <c>Deconstruct</c> for this in netstandard2.1, and these packages
    /// target netstandard2.0. Restored as one extension rather than by rewriting every loop that
    /// reads a YAML mapping, because the idiom is what makes those loops legible.
    /// </para>
    /// </summary>
    internal static class KeyValuePairDeconstruction
    {
        internal static void Deconstruct<TKey, TValue>(
            this KeyValuePair<TKey, TValue> pair, out TKey key, out TValue value)
        {
            key = pair.Key;
            value = pair.Value;
        }
    }
}
