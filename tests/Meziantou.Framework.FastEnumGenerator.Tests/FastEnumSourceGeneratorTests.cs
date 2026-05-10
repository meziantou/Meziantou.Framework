#pragma warning disable MA0101 // String contains an implicit end of line character
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Meziantou.Framework.FastEnumGenerator;
using TestUtilities;
using Xunit;

namespace Meziantou.Framework.FastEnumGenerator.Tests;

public sealed class FastEnumSourceGeneratorTests
{
    [Fact]
    public async Task GenerateCoreFiles()
    {
        var sourceCode = """
            [assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Sample.Color))]
            namespace Sample
            {
                public enum Color
                {
                    Blue,
                    Red,
                }
            }
            """;

        var (runResult, _) = await GenerateFiles(sourceCode);
        Assert.Empty(runResult.Diagnostics);
        Assert.Equal(3, runResult.GeneratedTrees.Length);
        Assert.Contains(runResult.GeneratedTrees, static tree => tree.FilePath.EndsWith("FastEnumExtensions.g.cs", StringComparison.Ordinal));
        Assert.Contains(runResult.GeneratedTrees, static tree => tree.FilePath.EndsWith("Microsoft.CodeAnalysis.EmbeddedAttribute.g.cs", StringComparison.Ordinal));
        Assert.Contains(runResult.GeneratedTrees, static tree => tree.FilePath.EndsWith("Meziantou.Framework.Annotations.FastEnumAttribute.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GenerateMetadataAwareMethods()
    {
        var sourceCode = """
            using System.ComponentModel.DataAnnotations;
            using System.ComponentModel;
            using System.Runtime.Serialization;

            [assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Sample.Color), ExtensionMethodNamespace = "Sample.Generated")]

            namespace System.ComponentModel
            {
                [global::System.AttributeUsage(global::System.AttributeTargets.Field)]
                public sealed class DisplayNameAttribute : global::System.Attribute
                {
                    public DisplayNameAttribute(string displayName) { DisplayName = displayName; }
                    public string DisplayName { get; }
                }
            }

            namespace Sample
            {
                [System.Flags]
                public enum Color
                {
                    [Display(Name = "Blue metadata")]
                    Blue = 1,
                    [EnumMember(Value = "Red metadata")]
                    Red = 2,
                    [DisplayName("Green metadata")]
                    Green = 4,
                }
            }
            """;

        var generatedCode = await GenerateCode(sourceCode);
        Assert.Contains("namespace Sample.Generated", generatedCode, StringComparison.Ordinal);
        Assert.Contains("ToStringFast(this global::Sample.Color value, bool useMetadata)", generatedCode, StringComparison.Ordinal);
        Assert.Contains("HasFlag(this global::Sample.Color instance, global::Sample.Color flag)", generatedCode, StringComparison.Ordinal);
        Assert.Contains("GetName(this global::Sample.Color instance)", generatedCode, StringComparison.Ordinal);
        Assert.Contains("\"Blue metadata\"", generatedCode, StringComparison.Ordinal);
        Assert.Contains("\"Red metadata\"", generatedCode, StringComparison.Ordinal);
        Assert.Contains("\"Green metadata\"", generatedCode, StringComparison.Ordinal);
        Assert.Contains("return (instance & flag) == flag;", generatedCode, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateCSharp14ExtensionMembers()
    {
        var sourceCode = """
            [assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Sample.Color))]
            namespace Sample
            {
                public enum Color
                {
                    Blue,
                    Red,
                }
            }
            """;

        var generatedCode = await GenerateCode(sourceCode, LanguageVersion.Preview);
        Assert.Contains("extension(global::Sample.Color)", generatedCode, StringComparison.Ordinal);
        Assert.Contains("static global::Sample.Color Parse(string value, bool ignoreCase)", generatedCode, StringComparison.Ordinal);
        Assert.Contains("static global::System.ReadOnlySpan<string> GetNames(bool useMetadata)", generatedCode, StringComparison.Ordinal);
        Assert.Contains("static global::System.ReadOnlySpan<global::Sample.Color> GetValues()", generatedCode, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DoNotGenerateCSharp14ExtensionMembersWhenDisabled()
    {
        var sourceCode = """
            [assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Sample.Color))]
            namespace Sample
            {
                public enum Color
                {
                    Blue,
                    Red,
                }
            }
            """;

        var generatedCode = await GenerateCode(sourceCode, LanguageVersion.CSharp12);
        Assert.DoesNotContain("extension(global::Sample.Color)", generatedCode, StringComparison.Ordinal);
        Assert.DoesNotContain("GetNames(bool useMetadata)", generatedCode, StringComparison.Ordinal);
        Assert.DoesNotContain("GetValues()", generatedCode, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnalyzerReportsNonEnumType()
    {
        var sourceCode = """
            [assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(string))]
            """;

        var diagnostics = await AnalyzeFiles(sourceCode);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("MFEG0001", diagnostic.Id);
        Assert.Contains("string", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnalyzerReportsNullType()
    {
        var sourceCode = """
            [assembly: Meziantou.Framework.Annotations.FastEnumAttribute(null)]
            """;

        var diagnostics = await AnalyzeFiles(sourceCode);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("MFEG0001", diagnostic.Id);
        Assert.Contains("(null)", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnalyzerDoesNotReportForEnumType()
    {
        var sourceCode = """
            [assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Sample.Color))]
            namespace Sample
            {
                public enum Color
                {
                    Blue,
                    Red,
                }
            }
            """;

        var diagnostics = await AnalyzeFiles(sourceCode);
        Assert.Empty(diagnostics);
    }

    private static async Task<string> GenerateCode(string sourceCode, LanguageVersion languageVersion = LanguageVersion.Preview)
    {
        var (runResult, _) = await GenerateFiles(sourceCode, languageVersion);
        var generatedTree = Assert.Single(runResult.GeneratedTrees, static tree => tree.FilePath.EndsWith("FastEnumExtensions.g.cs", StringComparison.Ordinal));
        return (await generatedTree.GetRootAsync()).ToFullString();
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeFiles(string sourceCode, LanguageVersion languageVersion = LanguageVersion.Preview)
    {
        var (_, compilation) = await GenerateFiles(sourceCode, languageVersion);
        var analyzer = new FastEnumAnalyzer();
        var diagnostics = await compilation
            .WithAnalyzers([analyzer])
            .GetAnalyzerDiagnosticsAsync();

        return [.. diagnostics
            .Where(static diagnostic => diagnostic.Id.StartsWith("MFEG", StringComparison.Ordinal))
            .OrderBy(static diagnostic => diagnostic.Id, StringComparer.Ordinal)];
    }

    private static async Task<(GeneratorDriverRunResult RunResult, Compilation Compilation)> GenerateFiles(string sourceCode, LanguageVersion languageVersion = LanguageVersion.Preview)
    {
        var netcoreReferences = await NuGetHelpers.GetNuGetReferences("Microsoft.NETCore.App.Ref", "8.0.0", "ref/net8.0/");
        var references = netcoreReferences.Select(static location => MetadataReference.CreateFromFile(location)).ToArray();
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(languageVersion);
        var compilation = CSharpCompilation.Create(
            "compilation",
            [CSharpSyntaxTree.ParseText(sourceCode, parseOptions)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new FastEnumSourceGenerator().AsSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create([generator], parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        Assert.Empty(diagnostics);
        using var stream = new MemoryStream();
        var emitResult = outputCompilation.Emit(stream);
        Assert.True(emitResult.Success, string.Join('\n', emitResult.Diagnostics));
        return (driver.GetRunResult(), outputCompilation);
    }
}
