using System.Collections.Generic;
using System.Linq;
using Utos.Workflows.V1.Validation;
using Xunit;

namespace Utos.Workflows.Validation.Tests;

/// <summary>
/// The static expression rules on their own, below the fixture level: how a templated string is
/// split, which form a field takes, and that a report says where in the expression it happened.
/// The codes themselves are pinned by the conformance corpus; these pin the mechanics.
/// </summary>
public class ExpressionRulesTests
{
    [Theory]
    [InlineData("{{ ({ a: { b: 1 } }).a.b }}")]                      // nested braces
    [InlineData("{{ '}}' }}")]                                        // the delimiter inside a string
    [InlineData("{{\n  const a = 1;\n  a + 1\n}}")]                   // multi-line program
    [InlineData("prefix {{ input.id }} middle {{ input.n + 1 }} end")]  // interpolation
    [InlineData("{{ input.a }}{{ input.b }}")]                        // adjacent segments
    [InlineData("literal text with no template")]
    [InlineData("")]
    public void Templates_the_splitter_accepts(string text)
    {
        var issues = new List<ValidationIssue>();
        Run(text, issues);
        Assert.Empty(issues);
    }

    [Theory]
    [InlineData("https://x/{{ input.id", ValidationCodes.ExpressionUnclosed)]
    [InlineData("{{ input.id }} and {{ input", ValidationCodes.ExpressionUnclosed)]
    [InlineData("{{ input.id === }}", ValidationCodes.ExpressionSyntaxError)]
    [InlineData("{{ }}", ValidationCodes.ExpressionNoValue)]
    [InlineData("{{ const a = 1; }}", ValidationCodes.ExpressionNoValue)]
    [InlineData("id-{{ const a = 1; a }}", ValidationCodes.ExpressionUnclosed)]   // statements in interpolation
    public void Templates_the_splitter_refuses(string text, string code)
    {
        var issues = new List<ValidationIssue>();
        Run(text, issues);
        Assert.Equal(code, Assert.Single(issues).Code);
    }

    [Fact]
    public void A_whole_field_is_one_program_and_may_be_multi_statement()
    {
        var issues = new List<ValidationIssue>();
        Run("{{ const ids = input.items.map(i => i.id); [...new Set(ids)] }}", issues);
        Assert.Empty(issues);
    }

    [Fact]
    public void Text_around_a_single_segment_makes_it_interpolation()
    {
        // `{{ a }} and {{ b }}` starts with {{ and ends with }} but is not one program.
        var issues = new List<ValidationIssue>();
        Run("{{ input.a }} and {{ input.b }}", issues);
        Assert.Empty(issues);

        issues.Clear();
        Run("{{ const a = 1; a }} and {{ input.b }}", issues);
        Assert.Equal(ValidationCodes.ExpressionUnclosed, Assert.Single(issues).Code);
    }

    [Fact]
    public void A_condition_carries_no_delimiters()
    {
        var issues = new List<ValidationIssue>();
        ExpressionRules.ValidateCondition("{{ output.ok }}", "p", issues);
        Assert.Equal(ValidationCodes.ExpressionDelimitedCondition, Assert.Single(issues).Code);

        issues.Clear();
        ExpressionRules.ValidateCondition("output.ok && output.count > 0", "p", issues);
        Assert.Empty(issues);
    }

    [Fact]
    public void A_field_reports_each_code_once_with_a_position()
    {
        var issues = new List<ValidationIssue>();
        ExpressionRules.ValidateCondition("let a = 0;\nwhile (false) {}\nwhile (false) {}\na === 0", "p", issues);

        var issue = Assert.Single(issues);
        Assert.Equal(ValidationCodes.ExpressionLoop, issue.Code);
        Assert.Equal("p", issue.Path);
        Assert.Contains("2:", issue.Message);   // the first loop, line 2
    }

    [Fact]
    public void Grammar_violations_inside_arrow_bodies_are_found()
    {
        var issues = new List<ValidationIssue>();
        Run("{{ input.items.map(i => { for (;;) {} return i; }) }}", issues);
        Assert.Contains(issues, i => i.Code == ValidationCodes.ExpressionLoop);
    }

    [Fact]
    public void The_language_accepts_what_the_workflows_need()
    {
        string[] programs =
        [
            "output.payload.headers.findLast(h => h.name === 'From')?.value ?? ''",
            "[...new Set(output.history.flatMap(h => h.messagesAdded ?? []).map(m => m.message.id))]",
            "const { name } = input.user; name",
            "input.items.reduce((acc, x) => { const y = x * 2; return acc + y; }, 0)",
            "input.items.toSorted((a, b) => a - b)",
            "let s = 0; input.items.forEach(x => { s += x; }); s",
            "typeof input.count === 'number' && 'name' in input.user",
            "utos.base64UrlDecode(input.data)",
            "input['co' + 'unt']",
            "-input.count",
            "JSON.stringify({ ...input.user, [input.key]: 1 })",
            "/<(.+)>/.exec(input.from)[1]",
        ];

        foreach (var program in programs)
        {
            var issues = new List<ValidationIssue>();
            Run("{{ " + program + " }}", issues);
            Assert.True(issues.Count == 0, program + " → " + string.Join("; ", issues.Select(i => i.Code)));
        }
    }

    private static void Run(string text, List<ValidationIssue> issues) =>
        ExpressionRules.ValidateTemplate(text, "p", issues);
}
