using System.Collections.Immutable;
using Meziantou.Framework.IconifySourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Meziantou.Framework.IconifyTests;

public sealed class IconifySourceGeneratorTests
{
    [Fact]
    public void Generate_CompilesAndUsesTheIcons()
    {
        var result = Generate(
            [("mdi.json", """{ "prefix": "mdi", "info": { "name": "Material Design Icons" }, "width": 24, "height": 24, "icons": { "home": { "body": "<path d=\"M1 2\"/>" } } }""", null)],
            """
            using Meziantou.Framework;
            public static class Usage
            {
                public static string Svg => Iconify.Mdi.Icons.Home.Svg;
                public static string Prefix => Iconify.Mdi.Prefix;
            }
            """);

        Assert.Empty(result.Diagnostics);
        var source = Assert.Single(result.Sources);
        Assert.Equal("Iconify.Mdi.g.cs", source.HintName);
        Assert.Contains("""public global::Meziantou.Framework.IconifyIcon Home => new("mdi", "home", global::System.Text.Encoding.UTF8.GetString("<path d=\"M1 2\"/>"u8), 0, 0, 24, 24);""", source.Text);
        Assert.Contains("internal static class IconifyMdiExtensions", source.Text);
        Assert.Contains("Material Design Icons", source.Text);
    }

    [Fact]
    public void Generate_IconNames()
    {
        var result = Generate(
            [("a.json", """
                {
                  "prefix": "material-symbols",
                  "icons": {
                    "account-circle-outline": { "body": "" },
                    "123": { "body": "" },
                    "1-2-3": { "body": "" },
                    "_1232": { "body": "" },
                    "to-string": { "body": "" },
                    "get-hash-code": { "body": "" },
                    "iconify-material-symbols-icons": { "body": "" },
                    "a.b_c": { "body": "" },
                    "---": { "body": "" },
                    "old": { "body": "", "hidden": true }
                  }
                }
                """, null)],
            """
            using Meziantou.Framework;
            public static class Usage
            {
                public static object[] Icons =>
                [
                    Iconify.MaterialSymbols.Icons.AccountCircleOutline,
                    Iconify.MaterialSymbols.Icons._123,
                    Iconify.MaterialSymbols.Icons._1233,
                    Iconify.MaterialSymbols.Icons._1232,
                    Iconify.MaterialSymbols.Icons.ToString2,
                    Iconify.MaterialSymbols.Icons.GetHashCode2,
                    Iconify.MaterialSymbols.Icons.IconifyMaterialSymbolsIcons2,
                    Iconify.MaterialSymbols.Icons.AB_c,
                    Iconify.MaterialSymbols.Icons._,
                    Iconify.MaterialSymbols.Icons.Old,
                ];
            }
            """);

        Assert.Empty(result.Diagnostics);
        var source = Assert.Single(result.Sources);
        Assert.Contains("""public global::Meziantou.Framework.IconifyIcon _123 => new("material-symbols", "123", """, source.Text);
        Assert.Contains("""public global::Meziantou.Framework.IconifyIcon _1233 => new("material-symbols", "1-2-3", """, source.Text);
        Assert.Contains("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]\n        public global::Meziantou.Framework.IconifyIcon Old => new(\"material-symbols\", \"old\", global::System.Text.Encoding.UTF8.GetString(\"\"u8), 0, 0, 16, 16, isHidden: true);", source.Text);
    }

    [Fact]
    public void Generate_ClassNameAndFilter()
    {
        var result = Generate(
            [("a.json", """{ "prefix": "mdi", "icons": { "home": { "body": "" }, "home-outline": { "body": "" }, "account": { "body": "" } }, "aliases": { "house": { "parent": "home" } } }""", new() { ["IconifyClassName"] = "MyIcons", ["IconifyIcons"] = "home*; house ;unknown-*" })],
            """
            using Meziantou.Framework;
            public static class Usage
            {
                public static object[] Icons => [Iconify.MyIcons.Icons.Home, Iconify.MyIcons.Icons.HomeOutline, Iconify.MyIcons.Icons.House];
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("MFICONIFY004", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("'unknown-*'", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        var source = Assert.Single(result.Sources);
        Assert.DoesNotContain("Account", source.Text);
    }

    [Fact]
    public void Generate_ReservedClassName()
    {
        var result = Generate(
            [("a.json", """{ "prefix": "load", "icons": { "a": { "body": "" } } }""", null)],
            """
            using Meziantou.Framework;
            public static class Usage
            {
                public static object Icon => Iconify.LoadIconSet.Icons.A;
            }
            """);

        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData(null, "internal")]
    [InlineData("internal", "internal")]
    [InlineData("Public", "public")]
    public void Generate_Visibility(string? visibility, string expected)
    {
        var result = Generate([("a.json", """{ "prefix": "a", "icons": {} }""", null)], "", visibility);
        Assert.Empty(result.Diagnostics);
        var source = Assert.Single(result.Sources);
        Assert.Contains(expected + " static class IconifyAExtensions", source.Text);
        Assert.Contains(expected + " readonly struct IconifyAIconSet", source.Text);
        Assert.Contains(expected + " readonly struct IconifyAIcons", source.Text);
    }

    [Fact]
    public void Generate_EscapesStrings()
    {
        var result = Generate(
            [("a.json", """{ "prefix": "a", "info": { "name": "A & <B>" }, "icons": { "b": { "body": "<text>\"\\\n\u2028\ud83d\ude00</text>" } } }""", null)],
            """
            using Meziantou.Framework;
            public static class Usage
            {
                public static string Body => Iconify.A.Icons.B.Body;
            }
            """);

        Assert.Empty(result.Diagnostics);
        var source = Assert.Single(result.Sources);
        Assert.Contains("""
            "<text>\"\\\n\u2028\uD83D\uDE00</text>"u8
            """, source.Text);
        Assert.Contains("A &amp; &lt;B&gt;", source.Text);
    }

    [Fact]
    public void Generate_LoneSurrogateBodyIsAStringLiteral()
    {
        var result = Generate(
            [("a.json", """{ "prefix": "a", "icons": { "b": { "body": "<text>\ud83d</text>" } } }""", null)],
            """
            using Meziantou.Framework;
            public static class Usage
            {
                public static string Body => Iconify.A.Icons.B.Body;
            }
            """);

        Assert.Empty(result.Diagnostics);
        var source = Assert.Single(result.Sources);
        Assert.Contains("""new("a", "b", "<text>\uD83D</text>", 0, 0, 16, 16);""", source.Text);
    }

    [Fact]
    public void Generate_InvalidJson()
    {
        var result = Generate([("invalid.json", """{ "prefix": "a" """, null)], "");
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("MFICONIFY001", diagnostic.Id);
        Assert.Equal("invalid.json", diagnostic.Location.GetLineSpan().Path);
        Assert.Empty(result.Sources);
    }

    [Fact]
    public void Generate_DuplicateClassName()
    {
        var result = Generate(
            [
                ("a.json", """{ "prefix": "a", "icons": {} }""", null),
                ("b.json", """{ "prefix": "b", "icons": {} }""", new() { ["IconifyClassName"] = "A" }),
            ],
            "");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("MFICONIFY003", diagnostic.Id);
        Assert.Equal("b.json", diagnostic.Location.GetLineSpan().Path);
        Assert.Single(result.Sources);
    }

    [Fact]
    public void Generate_IgnoresOtherAdditionalFiles()
    {
        var result = Generate([("a.json", """{ "prefix": "a", "icons": {} }""", new() { ["IconifyIconSet"] = "false" })], "");
        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.Sources);
    }

    private static GenerationResult Generate((string Path, string Content, Dictionary<string, string>? Metadata)[] files, string source, string? visibility = null)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => Path.GetFileName(path).StartsWith("System.", StringComparison.Ordinal) || Path.GetFileName(path) is "netstandard.dll" or "mscorlib.dll")
            .Select(path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(Iconify).Assembly.Location))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            "compilation",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var additionalTexts = files.Select(file => (AdditionalText)new TestAdditionalText(file.Path, file.Content)).ToImmutableArray();
        var globalOptions = new Dictionary<string, string>(StringComparer.Ordinal);
        if (visibility is not null)
        {
            globalOptions["build_property.IconifyVisibility"] = visibility;
        }

        var fileOptions = files.ToDictionary(
            file => file.Path,
            file =>
            {
                var options = new Dictionary<string, string>(StringComparer.Ordinal) { ["build_metadata.AdditionalFiles.IconifyIconSet"] = "true" };
                foreach (var (key, value) in file.Metadata ?? [])
                {
                    options["build_metadata.AdditionalFiles." + key] = value;
                }

                return options;
            },
            StringComparer.Ordinal);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new IconifySourceGenerator().AsSourceGenerator()],
            additionalTexts: additionalTexts,
            parseOptions: parseOptions,
            optionsProvider: new TestOptionsProvider(globalOptions, fileOptions));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);
        var runResult = driver.GetRunResult();

        // The generated code must compile without warnings
        var compilationDiagnostics = outputCompilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning).ToList();
        Assert.Empty(compilationDiagnostics);

        var sources = runResult.Results.SelectMany(result => result.GeneratedSources).Select(generated => (generated.HintName, Text: generated.SourceText.ToString())).ToList();
        return new GenerationResult(generatorDiagnostics, sources);
    }

    private sealed record GenerationResult(ImmutableArray<Diagnostic> Diagnostics, List<(string HintName, string Text)> Sources);

    private sealed class TestAdditionalText(string path, string content) : AdditionalText
    {
        public override string Path => path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(content);
    }

    private sealed class TestOptionsProvider(Dictionary<string, string> globalOptions, Dictionary<string, Dictionary<string, string>> fileOptions) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions => new TestOptions(globalOptions);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new TestOptions([]);

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new TestOptions(fileOptions.TryGetValue(textFile.Path, out var options) ? options : []);
    }

    private sealed class TestOptions(Dictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value) => values.TryGetValue(key, out value);
    }
}
