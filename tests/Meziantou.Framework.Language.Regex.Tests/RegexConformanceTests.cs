using System.Text.Json;

namespace Meziantou.Framework.Language.Regex.Tests;

/// <summary>
/// Replays patterns whose verdict was recorded from the engines themselves: V8 for JavaScript, PCRE2 10.47 for PCRE,
/// and glibc's <c>regcomp</c> for POSIX. Each record says whether the engine accepted the pattern and, when it did,
/// how many groups it captures and what they are named.
/// </summary>
/// <remarks>
/// <para>
/// Unlike the .NET differential test, these run on every target framework: the verdicts are frozen, so nothing here
/// depends on what the machine running the tests has installed. <c>Corpus/conformance.md</c> says how the files were
/// produced and which few engine behaviours were deliberately left out.
/// </para>
/// <para>
/// A test gathers every disagreement before failing, because one parser change usually moves many records at once and
/// the list is what shows the pattern behind them.
/// </para>
/// </remarks>
public sealed class RegexConformanceTests
{
    [Fact]
    public void JavaScript_AgreesWithV8()
    {
        var failures = new List<string>();
        var count = 0;
        foreach (var record in ReadRecords("JavaScriptConformance.jsonl"))
        {
            count++;
            var pattern = record.GetProperty("pattern").GetString()!;
            var flags = record.GetProperty("flags").GetString()!;
            var options = new RegexParseOptions(RegexDialect.JavaScript)
            {
                PatternOptions = flags switch
                {
                    "i" => RegexPatternOptions.IgnoreCase,
                    "u" => RegexPatternOptions.Unicode,
                    "v" => RegexPatternOptions.Unicode | RegexPatternOptions.UnicodeSets,
                    _ => RegexPatternOptions.None,
                },
            };

            var tree = RegexSyntaxAssert.TextIsFaithful(pattern, options);
            var label = $"/{pattern}/{flags}";
            if (!CheckValidity(tree, record, label, failures))
                continue;

            var expectedNames = record.GetProperty("names").EnumerateArray().Select(name => name.GetString()!).ToArray();
            var actualNames = tree.Captures.Select(capture => capture.Name).Where(name => !char.IsAsciiDigit(name[0])).Distinct(StringComparer.Ordinal).ToArray();
            CheckCaptures(tree.Captures.Count, record, label, failures);
            if (!expectedNames.SequenceEqual(actualNames, StringComparer.Ordinal))
            {
                failures.Add($"{label}: names [{string.Join(", ", actualNames)}], V8 has [{string.Join(", ", expectedNames)}]");
            }
        }

        Assert.NotEqual(0, count);
        Assert.Empty(failures, string.Join('\n', failures));
    }

    [Fact]
    public void Pcre_AgreesWithPcre2()
    {
        var failures = new List<string>();
        var count = 0;
        foreach (var record in ReadRecords("PcreConformance.jsonl"))
        {
            count++;
            var pattern = record.GetProperty("pattern").GetString()!;
            var extended = record.GetProperty("extended").GetBoolean();
            var options = new RegexParseOptions(RegexDialect.PcrePerl)
            {
                PatternOptions = extended ? RegexPatternOptions.IgnorePatternWhitespace : RegexPatternOptions.None,
            };

            var tree = RegexSyntaxAssert.TextIsFaithful(pattern, options);
            var label = extended ? $"/{pattern}/x" : $"/{pattern}/";
            if (!CheckValidity(tree, record, label, failures))
                continue;

            // PCRE reports the highest group number, which a branch reset group makes smaller than the group count.
            CheckCaptures(tree.Captures.Count == 0 ? 0 : tree.Captures.Max(capture => capture.Number), record, label, failures);

            // Every name with every number it is given, which duplicate names allowed by "(?J)" make more than one.
            var expectedNames = record.GetProperty("names").EnumerateArray()
                .Select(pair => FormattableString.Invariant($"{pair[0].GetString()}={pair[1].GetInt32()}"))
                .Order(StringComparer.Ordinal)
                .ToArray();
            var actualNames = tree.Captures
                .Where(capture => !char.IsAsciiDigit(capture.Name[0]))
                .Select(capture => FormattableString.Invariant($"{capture.Name}={capture.Number}"))
                .Order(StringComparer.Ordinal)
                .ToArray();

            if (!expectedNames.SequenceEqual(actualNames, StringComparer.Ordinal))
            {
                failures.Add($"{label}: names [{string.Join(", ", actualNames)}], PCRE2 has [{string.Join(", ", expectedNames)}]");
            }
        }

        Assert.NotEqual(0, count);
        Assert.Empty(failures, string.Join('\n', failures));
    }

    [Fact]
    public void Posix_AgreesWithGlibc()
    {
        var failures = new List<string>();
        var count = 0;
        foreach (var record in ReadRecords("PosixConformance.jsonl"))
        {
            count++;
            var pattern = record.GetProperty("pattern").GetString()!;
            var dialect = record.GetProperty("dialect").GetString() == "ere" ? RegexDialect.PosixExtended : RegexDialect.PosixBasic;

            var tree = RegexSyntaxAssert.TextIsFaithful(pattern, dialect);
            var label = $"{dialect.Name} [{pattern}]";
            if (CheckValidity(tree, record, label, failures))
            {
                CheckCaptures(tree.Captures.Count, record, label, failures);
            }
        }

        Assert.NotEqual(0, count);
        Assert.Empty(failures, string.Join('\n', failures));
    }

    /// <summary>Records a disagreement about validity, and returns whether the pattern is valid on both sides.</summary>
    private static bool CheckValidity(RegexSyntaxTree tree, JsonElement record, string label, List<string> failures)
    {
        var expectedValid = record.GetProperty("valid").GetBoolean();
        var diagnostics = tree.GetDiagnostics();
        if (expectedValid && diagnostics.Count > 0)
        {
            failures.Add($"{label}: the engine accepts it, the parser reported {string.Join(", ", diagnostics.Select(diagnostic => $"{diagnostic.Id} {diagnostic.Message}"))}");
        }
        else if (!expectedValid && diagnostics.Count == 0)
        {
            failures.Add($"{label}: the engine rejects it, the parser reported nothing");
        }

        return expectedValid && diagnostics.Count == 0;
    }

    private static void CheckCaptures(int actual, JsonElement record, string label, List<string> failures)
    {
        var expected = record.GetProperty("captures").GetInt32();
        if (actual != expected)
        {
            failures.Add(FormattableString.Invariant($"{label}: {actual} capture groups, the engine has {expected}"));
        }
    }

    private static List<JsonElement> ReadRecords(string name)
    {
        using var stream = typeof(RegexConformanceTests).Assembly.GetManifestResourceStream($"Meziantou.Framework.Language.Regex.Tests.Corpus.{name}")
            ?? throw new InvalidOperationException($"The corpus '{name}' is not embedded in the test assembly.");
        using var reader = new StreamReader(stream);

        var records = new List<JsonElement>();
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0)
            {
                using var document = JsonDocument.Parse(line);
                records.Add(document.RootElement.Clone());
            }
        }

        return records;
    }
}
