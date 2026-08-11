using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Google.Protobuf;
using Utos.Workflows.V1;
using Utos.Workflows.V1.Validation;
using Xunit;

namespace Utos.Workflows.Validation.Tests;

/// <summary>
/// Runs the cross-implementation fixtures vendored from <c>utos/api</c>. These are the source of
/// truth for what this library must report — every SDK runs the same corpus, so "does sdk-x agree
/// with sdk-y" is an answerable question rather than a code review.
/// <para>
/// Only <c>code</c> and <c>path</c> are asserted. Message text is deliberately not contractual.
/// </para>
/// </summary>
public class ConformanceTests
{
    public static TheoryData<string> ValidCases => Cases("valid");

    public static TheoryData<string> InvalidCases => Cases("invalid");

    [Theory]
    [MemberData(nameof(ValidCases))]
    public void Valid_fixtures_produce_no_issues(string name)
    {
        var bundle = ReadBundle(Path.Combine(FixtureRoot, "valid", name + ".json"));

        var report = WorkflowBundleValidator.Validate(bundle);

        Assert.True(report.IsValid,
            $"expected '{name}' to be valid, but got:{Environment.NewLine}{report}");
    }

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public void Invalid_fixtures_produce_exactly_the_expected_issues(string name)
    {
        var bundle = ReadBundle(Path.Combine(FixtureRoot, "invalid", name + ".json"));
        var expected = ReadExpected(Path.Combine(FixtureRoot, "invalid", name + ".expected.json"));

        var actual = WorkflowBundleValidator.Validate(bundle).Issues
            .Select(i => (i.Code, i.Path))
            .OrderBy(i => i.Code, StringComparer.Ordinal)
            .ThenBy(i => i.Path, StringComparer.Ordinal)
            .ToList();

        // The set must match exactly — extra issues are as much a conformance failure as missing
        // ones, since they would make a bundle one tool accepts another reject.
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Guards against the corpus silently disappearing — a wrong path would otherwise make every
    /// theory above vacuously pass with zero cases.
    /// </summary>
    [Fact]
    public void Corpus_is_present()
    {
        Assert.True(Directory.Exists(FixtureRoot), $"fixture directory not found: {FixtureRoot}");
        Assert.NotEmpty(CaseNames("valid"));
        Assert.NotEmpty(CaseNames("invalid"));

        // Every invalid fixture must be paired with its expectations.
        foreach (var name in CaseNames("invalid"))
        {
            Assert.True(File.Exists(Path.Combine(FixtureRoot, "invalid", name + ".expected.json")),
                $"'{name}.json' has no matching .expected.json");
        }
    }

    private static TheoryData<string> Cases(string bucket)
    {
        var data = new TheoryData<string>();
        foreach (var name in CaseNames(bucket)) data.Add(name);
        return data;
    }

    private static List<string> CaseNames(string bucket)
    {
        var directory = Path.Combine(FixtureRoot, bucket);
        if (!Directory.Exists(directory)) return new List<string>();

        return Directory.EnumerateFiles(directory, "*.json")
            .Where(f => !f.EndsWith(".expected.json", StringComparison.Ordinal))
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList()!;
    }

    private static WorkflowBundle ReadBundle(string path) =>
        JsonParser.Default.Parse<WorkflowBundle>(File.ReadAllText(path));

    private static List<(string Code, string Path)> ReadExpected(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        return document.RootElement.GetProperty("issues").EnumerateArray()
            .Select(e => (Code: e.GetProperty("code").GetString()!, Path: e.GetProperty("path").GetString()!))
            .OrderBy(i => i.Code, StringComparer.Ordinal)
            .ThenBy(i => i.Path, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The corpus is vendored from <c>utos/api</c> into the repo root by the release workflow,
    /// alongside <c>proto/</c>. Locating it from this file's compile-time path rather than from the
    /// test binary's working directory keeps it correct however the tests are launched.
    /// </summary>
    private static string FixtureRoot { get; } =
        Path.GetFullPath(Path.Combine(ThisDirectory(), "..", "..", "conformance", "validation"));

    private static string ThisDirectory([CallerFilePath] string path = "") =>
        Path.GetDirectoryName(path)!;
}
