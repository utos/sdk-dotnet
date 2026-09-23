using System;
using System.Globalization;

namespace Utos.Workflows.V1
{
    /// <summary>
    /// The duration syntax of <c>api/docs/workflow-source-format.md</c> § Durations: a string in
    /// the unit shorthand — <c>90s</c>, <c>8h</c>, <c>1h30m</c>, <c>2d12h</c> — in place of the
    /// <c>google.protobuf.Duration</c> a timer carried until spec 0.20.0.
    /// <para>
    /// One parser, here, because three things have to agree about what <c>1h30m</c> means: the
    /// front end that reads a document, the validator that checks a literal at load
    /// (<c>UTOS-C202</c>, <c>UTOS-C203</c>), and the daemon that renders a template and enters a
    /// timer with the result (<c>UTOS-E106</c>). A second implementation of the grammar is a
    /// second set of edge cases.
    /// </para>
    /// <para>
    /// Deliberately strict, in the ways that let two implementations disagree: whole numbers only
    /// (<c>1.5h</c> is <c>1h30m</c>, and Go's parser accepts fractions that round differently);
    /// units largest first and at most once each; no spaces; no weeks, months or years, because a
    /// month has no fixed length; and no ISO 8601, so there is one spelling rather than two.
    /// <c>86400s</c> stays valid, which is what every timer written before 0.20.0 contains.
    /// </para>
    /// </summary>
    public static class WorkflowDuration
    {
        /// <summary>Why a duration string was refused. <c>None</c> means it was accepted.</summary>
        public enum Failure
        {
            None = 0,

            /// <summary>Empty, or not the unit shorthand at all: <c>UTOS-C203</c>.</summary>
            Syntax,

            /// <summary>Well formed and zero, or so large it cannot be held: <c>UTOS-C202</c>.</summary>
            NotPositive,
        }

        // Largest first, which is also the order a valid string writes them in. The ordering is
        // the grammar: `1h30m` is legal and `30m1h` is not, so a parser that accepted any order
        // would accept strings another implementation refuses.
        private static readonly (string Unit, long Ticks)[] Units =
        {
            ("d", TimeSpan.TicksPerDay),
            ("h", TimeSpan.TicksPerHour),
            ("m", TimeSpan.TicksPerMinute),
            ("s", TimeSpan.TicksPerSecond),
            ("ms", TimeSpan.TicksPerMillisecond),
        };

        /// <summary>
        /// Parses a duration in the unit shorthand. Returns false and sets <paramref name="failure"/>
        /// when the string is not one, or is not positive.
        /// </summary>
        public static bool TryParse(string text, out TimeSpan value, out Failure failure)
        {
            value = default;
            failure = Failure.Syntax;

            if (string.IsNullOrEmpty(text)) return false;

            long ticks = 0;
            int i = 0;
            int unit = 0;          // the next unit that may appear: units never repeat or go back
            bool any = false;

            while (i < text.Length)
            {
                int digitsStart = i;
                while (i < text.Length && text[i] >= '0' && text[i] <= '9') i++;
                if (i == digitsStart) return false;                       // a unit with no number

                int unitStart = i;
                while (i < text.Length && (text[i] < '0' || text[i] > '9')) i++;
                string suffix = text.Substring(unitStart, i - unitStart);
                if (suffix.Length == 0) return false;                     // a number with no unit

                int found = -1;
                for (int u = unit; u < Units.Length; u++)
                {
                    if (string.Equals(Units[u].Unit, suffix, StringComparison.Ordinal)) { found = u; break; }
                }
                if (found < 0) return false;    // unknown unit, or one already used, or out of order

                if (!long.TryParse(text.Substring(digitsStart, unitStart - digitsStart),
                        NumberStyles.None, CultureInfo.InvariantCulture, out long amount))
                {
                    // More digits than a long holds. Not a syntax problem: it is a duration nothing
                    // can wait for.
                    failure = Failure.NotPositive;
                    return false;
                }

                try
                {
                    checked { ticks += amount * Units[found].Ticks; }
                }
                catch (OverflowException)
                {
                    failure = Failure.NotPositive;
                    return false;
                }

                unit = found + 1;
                any = true;
            }

            if (!any) return false;

            if (ticks <= 0)
            {
                failure = Failure.NotPositive;
                return false;
            }

            value = TimeSpan.FromTicks(ticks);
            failure = Failure.None;
            return true;
        }

        /// <summary>True when <paramref name="text"/> is a positive duration in the syntax.</summary>
        public static bool IsValid(string text) => TryParse(text, out _, out _);
    }
}
