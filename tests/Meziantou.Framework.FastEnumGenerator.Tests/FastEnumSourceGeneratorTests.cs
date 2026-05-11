#pragma warning disable MA0101 // String contains an implicit end of line character
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
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
        var generatedTree = Assert.Single(runResult.GeneratedTrees, static tree => tree.FilePath.EndsWith("FastEnumExtensions.g.cs", StringComparison.Ordinal));
        Assert.Contains(runResult.GeneratedTrees, static tree => tree.FilePath.EndsWith("Microsoft.CodeAnalysis.EmbeddedAttribute.g.cs", StringComparison.Ordinal));
        Assert.Contains(runResult.GeneratedTrees, static tree => tree.FilePath.EndsWith("Meziantou.Framework.Annotations.FastEnumAttribute.g.cs", StringComparison.Ordinal));

        var generatedCode = (await generatedTree.GetRootAsync()).ToFullString();
        Assert.DoesNotContain("return useMetadata ? s_definedMetadataNames_", generatedCode, StringComparison.Ordinal);
        Assert.Contains("return s_names_", generatedCode, StringComparison.Ordinal);
        Assert.DoesNotContain("private static ulong ToUInt64_", generatedCode, StringComparison.Ordinal);
        Assert.DoesNotContain("private static global::Sample.Color FromUInt64_", generatedCode, StringComparison.Ordinal);
        Assert.DoesNotContain("s_parseTokenIsMetadata_", generatedCode, StringComparison.Ordinal);
        Assert.DoesNotContain("s_definedNames_", generatedCode, StringComparison.Ordinal);
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
        Assert.Contains("s_parseMetadataTokens_", generatedCode, StringComparison.Ordinal);
        Assert.DoesNotContain("s_parseTokenIsMetadata_", generatedCode, StringComparison.Ordinal);
        Assert.Contains("var separatorIndex = global::System.MemoryExtensions.IndexOf(remaining, ',');", generatedCode, StringComparison.Ordinal);
        Assert.Contains("token = global::System.MemoryExtensions.Trim(token);", generatedCode, StringComparison.Ordinal);
        Assert.DoesNotContain("TrimToken_", generatedCode, StringComparison.Ordinal);
        Assert.Contains("TryParseMetadataIgnoreCase_", generatedCode, StringComparison.Ordinal);
        Assert.Contains("if (IsNumericToken_", generatedCode, StringComparison.Ordinal);
        Assert.Contains("EqualsTokenOrdinalIgnoreCase_", generatedCode, StringComparison.Ordinal);
        Assert.DoesNotContain("EqualsToken_", generatedCode, StringComparison.Ordinal);
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

    public static TheoryData<string, string> AnalyzerRuleCases { get; } = new()
    {
        { "_ = Enum.Parse<Sample.Color>(\"Blue\", false);", "MFEG0002" },
        { "_ = Enum.TryParse<Sample.Color>(\"Blue\", out var parsed);", "MFEG0003" },
        { "_ = Enum.GetNames<Sample.Color>();", "MFEG0004" },
        { "_ = Enum.GetValues<Sample.Color>();", "MFEG0005" },
        { "_ = Enum.GetName(Sample.Color.Blue);", "MFEG0006" },
        { "_ = Enum.IsDefined(Sample.Color.Blue);", "MFEG0007" },
        { "_ = Sample.Color.Blue.ToString();", "MFEG0008" },
    };

    [Theory]
    [MemberData(nameof(AnalyzerRuleCases))]
    public async Task AnalyzerReportsFastEnumApis(string invocation, string expectedDiagnosticId)
    {
        var sourceCode = CreateAnalyzerSource(invocation, useFastEnumAttribute: true);
        var diagnostics = await AnalyzeFiles(sourceCode);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(expectedDiagnosticId, diagnostic.Id);
    }

    [Fact]
    public async Task AnalyzerDoesNotReportEnumApisForNonFastEnum()
    {
        var sourceCode = CreateAnalyzerSource("_ = Enum.Parse<Sample.Color>(\"Blue\", false);", useFastEnumAttribute: false);
        var diagnostics = await AnalyzeFiles(sourceCode);
        Assert.Empty(diagnostics);
    }

    public static TheoryData<string, string, string> CodeFixCases { get; } = new()
    {
        { "_ = Enum.Parse<Sample.Color>(\"Blue\", false);", "MFEG0002", "global::Sample.Color.Parse(\"Blue\", false)" },
        { "_ = Enum.TryParse<Sample.Color>(\"Blue\", out var parsed);", "MFEG0003", "global::Sample.Color.TryParse(\"Blue\", ignoreCase: false, out var parsed)" },
        { "_ = Enum.GetNames<Sample.Color>();", "MFEG0004", "global::Sample.Color.GetNames(useMetadata: false)" },
        { "_ = Enum.GetValues<Sample.Color>();", "MFEG0005", "global::Sample.Color.GetValues()" },
        { "_ = Enum.GetName(Sample.Color.Blue);", "MFEG0006", "Sample.Color.Blue.GetName()" },
        { "_ = Enum.IsDefined(Sample.Color.Blue);", "MFEG0007", "global::Sample.Color.IsDefined(Sample.Color.Blue)" },
        { "_ = Sample.Color.Blue.ToString();", "MFEG0008", "Sample.Color.Blue.ToStringFast()" },
    };

    [Theory]
    [MemberData(nameof(CodeFixCases))]
    public async Task CodeFixRewritesFastEnumApis(string invocation, string expectedDiagnosticId, string expectedInvocation)
    {
        var sourceCode = CreateCodeFixSource(invocation);
        var fixedSource = await ApplyCodeFixAsync(sourceCode, expectedDiagnosticId);
        Assert.Contains(expectedInvocation, fixedSource, StringComparison.Ordinal);
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

    private static string CreateAnalyzerSource(string invocation, bool useFastEnumAttribute)
    {
        return $$"""
            using System;

            {{(useFastEnumAttribute ? "[assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Sample.Color))]" : "")}}

            namespace Sample
            {
                public enum Color
                {
                    Blue,
                    Red,
                }

                public static class TestClass
                {
                    public static void M()
                    {
                        {{invocation}}
                    }
                }
            }
            """;
    }

    private static string CreateCodeFixSource(string invocation)
    {
        return $$"""
            using System;

            [assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Sample.Color))]

            namespace Meziantou.Framework.Annotations
            {
                [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
                internal sealed class FastEnumAttribute : Attribute
                {
                    public FastEnumAttribute(Type enumType)
                    {
                    }
                }
            }

            namespace Sample
            {
                public enum Color
                {
                    Blue,
                    Red,
                }

                public static class TestClass
                {
                    public static void M()
                    {
                        {{invocation}}
                    }
                }
            }
            """;
    }

    private static async Task<string> ApplyCodeFixAsync(string sourceCode, string diagnosticId)
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);
        var netcoreReferences = await NuGetHelpers.GetNuGetReferences("Microsoft.NETCore.App.Ref", "8.0.0", "ref/net8.0/");
        var metadataReferences = netcoreReferences.Select(static location => MetadataReference.CreateFromFile(location)).ToArray();

        var solution = workspace.CurrentSolution
            .AddProject(projectId, "Project", "Project", LanguageNames.CSharp)
            .WithProjectParseOptions(projectId, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview))
            .WithProjectCompilationOptions(projectId, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable))
            .AddDocument(documentId, "Test.cs", SourceText.From(sourceCode, Encoding.UTF8));

        foreach (var metadataReference in metadataReferences)
        {
            solution = solution.AddMetadataReference(projectId, metadataReference);
        }

        var project = solution.GetProject(projectId) ?? throw new InvalidOperationException("Project should be available.");
        var document = project.GetDocument(documentId) ?? throw new InvalidOperationException("Document should be available.");
        var compilation = await project.GetCompilationAsync() ?? throw new InvalidOperationException("Compilation should be available.");

        var diagnostics = await compilation
            .WithAnalyzers([new FastEnumAnalyzer()])
            .GetAnalyzerDiagnosticsAsync();

        var diagnostic = Assert.Single(diagnostics, diag => diag.Id == diagnosticId);
        var codeFixProvider = new FastEnumCodeFixProvider();
        var codeActions = new List<CodeAction>();
        var codeFixContext = new CodeFixContext(document, diagnostic, (action, _) => codeActions.Add(action), CancellationToken.None);
        await codeFixProvider.RegisterCodeFixesAsync(codeFixContext);
        var codeAction = Assert.Single(codeActions);
        var operation = Assert.Single((await codeAction.GetOperationsAsync(CancellationToken.None)).OfType<ApplyChangesOperation>());
        var changedDocument = operation.ChangedSolution.GetDocument(documentId) ?? throw new InvalidOperationException("Changed document should be available.");
        return (await changedDocument.GetTextAsync()).ToString();
    }
}
