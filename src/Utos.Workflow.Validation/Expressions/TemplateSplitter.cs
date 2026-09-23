using System;
using System.Collections.Generic;
using Acornima;
using Acornima.Ast;

namespace Utos.Workflows.V1.Validation
{
    /// <summary>One piece of a templated string: literal text, or a parsed <c>{{ }}</c> program.</summary>
    internal sealed class TemplateSegment
    {
        public TemplateSegment(bool isExpression, string text, int offset, Program program)
        {
            IsExpression = isExpression;
            Text = text;
            Offset = offset;
            Program = program;
        }

        public bool IsExpression { get; }

        /// <summary>The literal text, or the program source between the delimiters.</summary>
        public string Text { get; }

        /// <summary>Where <see cref="Text"/> starts in the field.</summary>
        public int Offset { get; }

        /// <summary>The parsed program; null for a text segment.</summary>
        public Program Program { get; }
    }

    /// <summary>
    /// Splits a templated string into text and <c>{{ }}</c> segments the way
    /// <c>api/docs/template-expressions.md</c> § Forms defines it: at <c>{{</c>, one complete
    /// program is parsed and must be followed by <c>}}</c>. There is no scanning for the closing
    /// delimiter — it is found by parsing — which is what lets <c>{{ {a: {b: 1}} }}</c> be one
    /// program containing an object literal, and <c>{{ '}}' }}</c> contain its own delimiter.
    /// </summary>
    internal static class TemplateSplitter
    {
        private const string Open = "{{";
        private const string Close = "}}";

        /// <summary>
        /// Scans <paramref name="text"/>. On success the segments alternate freely between text
        /// and expression; on failure <paramref name="code"/> is <c>UTOS-E062</c> (a <c>{{</c>
        /// with no <c>}}</c> after a complete program) or <c>UTOS-E060</c> (a <c>}}</c> exists but
        /// nothing before any of them parses).
        /// </summary>
        public static bool TryScan(string text, out List<TemplateSegment> segments,
            out string code, out string message)
        {
            segments = new List<TemplateSegment>();
            code = null;
            message = null;

            int position = 0;
            while (true)
            {
                int open = text.IndexOf(Open, position, StringComparison.Ordinal);
                if (open < 0)
                {
                    if (position < text.Length)
                        segments.Add(new TemplateSegment(false, text.Substring(position), position, null));
                    return true;
                }

                if (open > position)
                    segments.Add(new TemplateSegment(false, text.Substring(position, open - position), position, null));

                int contentStart = open + Open.Length;
                int close = text.IndexOf(Close, contentStart, StringComparison.Ordinal);
                if (close < 0)
                {
                    code = ValidationCodes.ExpressionUnclosed;
                    message = "'{{' at offset " + open + " is never closed by '}}'.";
                    return false;
                }

                string lastError = null;
                Program parsed = null;
                int parsedEnd = -1;

                // Try each candidate '}}' from the left: the first one after which the content
                // parses as a program is the delimiter. A '}}' inside a string literal or an
                // object literal simply fails to parse and is passed over.
                while (close >= 0)
                {
                    string candidate = text.Substring(contentStart, close - contentStart);
                    try
                    {
                        parsed = ExpressionParser.Parse(candidate);
                        parsedEnd = close;
                        break;
                    }
                    catch (SyntaxErrorException error)
                    {
                        lastError = error.Message;
                    }

                    close = text.IndexOf(Close, close + 1, StringComparison.Ordinal);
                }

                if (parsed == null)
                {
                    code = ValidationCodes.ExpressionSyntaxError;
                    message = "The expression starting at offset " + open + " does not parse: " + lastError;
                    return false;
                }

                segments.Add(new TemplateSegment(true, text.Substring(contentStart, parsedEnd - contentStart),
                    contentStart, parsed));
                position = parsedEnd + Close.Length;
            }
        }
    }

    internal static class ExpressionParser
    {
        /// <summary>Parses a program as a strict-mode script — the same parse the engine performs.</summary>
        /// <remarks>
        /// <c>AllowAwaitOutsideFunction</c> is what spec 0.20.0 means by "await is permitted at a
        /// program's top level, as the body of an async arrow is": a program is not a module, so a
        /// stock script parse would refuse the one place a blob's bytes are read. The engine parses
        /// with the same option, so the grammar is checked on the tree the engine runs.
        /// </remarks>
        public static Program Parse(string source) =>
            new Parser(new ParserOptions { AllowAwaitOutsideFunction = true })
                .ParseScript(source, strict: true);
    }
}
