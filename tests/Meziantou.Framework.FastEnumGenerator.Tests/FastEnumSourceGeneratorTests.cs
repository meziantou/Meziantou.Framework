#pragma warning disable MA0101 // String contains an implicit end of line character
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
            using System.Runtime.Serialization;

            [assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Sample.Color), ExtensionMethodNamespace = "Sample.Generated")]
            namespace Sample
            {
                [System.Flags]
                public enum Color
                {
                    [Display(Name = "Blue metadata")]
                    Blue = 1,
                    [EnumMember(Value = "Red metadata")]
                    Red = 2,
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
    }

    [Fact]
    public async Task GenerateLegacyAttributeCompatibility()
    {
        var sourceCode = """
            [assembly: Meziantou.Framework.Annotations.FastEnumToStringAttribute(typeof(Sample.Color))]
            namespace Sample
            {
                public enum Color { Blue }
            }
            """;

        var generatedCode = await GenerateCode(sourceCode);
        Assert.Contains("ToStringFast(this global::Sample.Color value)", generatedCode, StringComparison.Ordinal);
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
        Assert.Contains("static global::Sample.Color[] GetValues()", generatedCode, StringComparison.Ordinal);
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

    private static async Task<string> GenerateCode(string sourceCode, LanguageVersion languageVersion = LanguageVersion.Preview)
    {
        var (runResult, _) = await GenerateFiles(sourceCode, languageVersion);
        var generatedTree = Assert.Single(runResult.GeneratedTrees, static tree => tree.FilePath.EndsWith("FastEnumExtensions.g.cs", StringComparison.Ordinal));
        return (await generatedTree.GetRootAsync()).ToFullString();
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
