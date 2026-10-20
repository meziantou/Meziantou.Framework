using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using ValueTagAnalyzerType = Meziantou.Framework.TaggedValues.Analyzer.ValueTagAnalyzer;

namespace Meziantou.Framework.Tests;

/// <summary>
/// The readme examples are compiled by samples/Meziantou.Framework.TaggedValues.Samples, which does not run the analyzer.
/// These tests run it on them, and check that it reports the diagnostics named by their comments, and only those.
/// </summary>
public sealed partial class ReadmeExampleTests : TaggedValuesAnalyzerTestBase
{
    private const string ResourcePrefix = "ReadmeSamples/";

    // The implicit usings of the sample project
    private const string GlobalUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using Meziantou.Framework.TaggedValues;
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadmeExample_ReportsTheDiagnosticsOfTheComments(bool strict)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var trees = GetSampleFiles()
            .Where(file => file.Strict == strict)
            .Select(file => CSharpSyntaxTree.ParseText(file.Text, parseOptions, path: file.Name, cancellationToken: XunitCancellationToken))
            .ToList();
        Assert.NotEmpty(trees);

        var compilation = CSharpCompilation.Create(
            "Samples",
            [.. trees, CSharpSyntaxTree.ParseText(GlobalUsings, parseOptions, path: "GlobalUsings.cs", cancellationToken: XunitCancellationToken)],
            await GetReferencesAsync(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        Assert.Empty(compilation.GetDiagnostics(XunitCancellationToken).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var analyzerOptions = new AnalyzerOptions([], new StrictModeOptionsProvider(strict));
        var diagnostics = await compilation
            .WithAnalyzers([new ValueTagAnalyzerType()], analyzerOptions)
            .GetAnalyzerDiagnosticsAsync(XunitCancellationToken);

        var expected = trees.SelectMany(GetExpectedDiagnostics).Order(StringComparer.Ordinal);
        var actual = diagnostics.Select(FormatReportedDiagnostic).Order(StringComparer.Ordinal);
        Assert.Equal(expected, actual);
    }

    private static List<(string Name, string Text, bool Strict)> GetSampleFiles()
    {
        var assembly = typeof(ReadmeExampleTests).Assembly;
        var result = new List<(string Name, string Text, bool Strict)>();
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            var path = name[ResourcePrefix.Length..].Replace('\\', '/');
            result.Add((path, reader.ReadToEnd(), path.StartsWith("Strict/", StringComparison.Ordinal)));
        }

        return result;
    }

    // A comment such as "// MFTV0002" names the diagnostics reported on its line
    private static IEnumerable<string> GetExpectedDiagnostics(SyntaxTree tree)
    {
        foreach (var line in tree.GetText(XunitCancellationToken).Lines)
        {
            var text = line.ToString();
            var commentIndex = text.IndexOf("//", StringComparison.Ordinal);
            if (commentIndex < 0)
                continue;

            foreach (Match match in DiagnosticIdRegex().Matches(text[commentIndex..]))
            {
                yield return FormatDiagnostic(tree.FilePath, line.LineNumber, match.Value);
            }
        }
    }

    // A diagnostic reported on a comment line, such as a "// ValueTag=..." comment, is named by the comment of the
    // statement that follows it
    private static string FormatReportedDiagnostic(Diagnostic diagnostic)
    {
        var tree = diagnostic.Location.SourceTree!;
        var lines = tree.GetText(XunitCancellationToken).Lines;
        var lineNumber = diagnostic.Location.GetLineSpan().StartLinePosition.Line;
        while (lineNumber < lines.Count - 1 && IsCommentOrBlankLine(lines[lineNumber]))
        {
            lineNumber++;
        }

        return FormatDiagnostic(tree.FilePath, lineNumber, diagnostic.Id);

        static bool IsCommentOrBlankLine(TextLine line)
        {
            var text = line.ToString().TrimStart();
            return text.Length == 0 || text.StartsWith("//", StringComparison.Ordinal);
        }
    }

    private static string FormatDiagnostic(string path, int lineNumber, string id)
    {
        return FormattableString.Invariant($"{path}({lineNumber + 1}): {id}");
    }

    [GeneratedRegex("MFTV[0-9]{4}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DiagnosticIdRegex();

    private sealed class StrictModeOptionsProvider(bool strict) : AnalyzerConfigOptionsProvider
    {
        private readonly StrictModeOptions _options = new(strict);

        public override AnalyzerConfigOptions GlobalOptions => _options;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _options;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _options;
    }

    private sealed class StrictModeOptions(bool strict) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
        {
            value = key == "taggedvalues.strict" ? (strict ? "true" : "false") : null;
            return value is not null;
        }
    }
}
