using System.Collections.Immutable;
using System.Reflection;
using Meziantou.Framework.FixedStringBuilder.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using TestUtilities;
using Xunit;

namespace Meziantou.Framework.FixedStringBuilder.Generator.Tests;

public sealed class FixedStringBuilderSourceGeneratorTests
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Preview);

    [Fact]
    public async Task GeneratesFixedStringFromAttribute()
    {
        const string source = """
            [FixedStringBuilderAttribute(10)]
            public partial struct FixedStringBuilder10
            {
            }

            public static class Harness
            {
                public static string GetValue()
                {
                    FixedStringBuilder10 value = "0123456789";
                    return value.ToString();
                }

                public static int GetLength()
                {
                    FixedStringBuilder10 value = "0123456789";
                    return value.Length;
                }

                public static void CreateTooLong()
                {
                    FixedStringBuilder10 _ = "0123456789ABC";
                }
            }
            """;

        var (runResult, compilation) = await GenerateAsync(source);
        Assert.Empty(runResult.Diagnostics);
        Assert.Single(runResult.Results);
        Assert.Equal(3, runResult.Results[0].GeneratedSources.Length);

        var allGeneratedSources = string.Join("\n", runResult.Results[0].GeneratedSources.Select(static source => source.SourceText.ToString()));
        Assert.Contains("internal partial class FixedStringBuilderAttribute", allGeneratedSources, StringComparison.Ordinal);
        Assert.Contains("internal sealed partial class EmbeddedAttribute", allGeneratedSources, StringComparison.Ordinal);
        Assert.Contains("public static int MaxLength => 10;", allGeneratedSources, StringComparison.Ordinal);
        Assert.Contains("private char _c0;", allGeneratedSources, StringComparison.Ordinal);
        Assert.Contains("public readonly ReadOnlySpan<char> AsSpan() => MemoryMarshal.CreateSpan(ref Unsafe.AsRef(in _c0), _length);", allGeneratedSources, StringComparison.Ordinal);
        Assert.Contains("public bool Equals(FixedStringBuilder10 other, StringComparison comparison)", allGeneratedSources, StringComparison.Ordinal);

        using var peStream = new MemoryStream();
        var emitResult = compilation.Emit(peStream);
        var diagnostics = string.Join('\n', emitResult.Diagnostics);
        Assert.True(emitResult.Success, diagnostics);

        var assembly = Assembly.Load(peStream.ToArray());
        var harnessType = assembly.GetType("Harness");
        var getValueMethod = harnessType?.GetMethod("GetValue", BindingFlags.Public | BindingFlags.Static);
        var getLengthMethod = harnessType?.GetMethod("GetLength", BindingFlags.Public | BindingFlags.Static);
        var createTooLongMethod = harnessType?.GetMethod("CreateTooLong", BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(getValueMethod);
        Assert.NotNull(getLengthMethod);
        Assert.NotNull(createTooLongMethod);
        Assert.Equal("0123456789", (string?)getValueMethod!.Invoke(null, null));
        Assert.Equal(10, (int)getLengthMethod!.Invoke(null, null)!);
        var exception = Assert.Throws<TargetInvocationException>(() => createTooLongMethod!.Invoke(null, null));
        Assert.IsType<ArgumentException>(exception.InnerException);
    }

    [Fact]
    public async Task ImplementsIFixedStringWhenInterfaceExists()
    {
        const string source = """
            namespace Meziantou.Framework.FixedStringBuilder
            {
                public interface IFixedString
                {
                    global::System.Span<char> GetUnsafeFullSpan();
                }

                public interface IFixedString<T> : IFixedString where T : IFixedString<T>
                {
                    static abstract int MaxLength { get; }
                    int Length { get; }
                    void Clear();
                    static abstract implicit operator T(string value);
                }
            }

            [FixedStringBuilderAttribute(4)]
            public partial struct FixedStringBuilder4
            {
            }

            public static class Harness
            {
                public static bool ImplementsGenericInterface() => default(FixedStringBuilder4) is Meziantou.Framework.FixedStringBuilder.IFixedString<FixedStringBuilder4>;

                public static int GetUnsafeSpanLength()
                {
                    Meziantou.Framework.FixedStringBuilder.IFixedString value = default(FixedStringBuilder4);
                    return value.GetUnsafeFullSpan().Length;
                }
            }
            """;

        var (runResult, compilation) = await GenerateAsync(source);
        Assert.Empty(runResult.Diagnostics);

        var generatedCode = string.Join("\n", runResult.Results[0].GeneratedSources.Select(static source => source.SourceText.ToString()));
        Assert.Contains("global::Meziantou.Framework.FixedStringBuilder.IFixedString<global::FixedStringBuilder4>", generatedCode, StringComparison.Ordinal);
        Assert.Contains("global::Meziantou.Framework.FixedStringBuilder.IFixedString.GetUnsafeFullSpan() => AsUnsafeFullSpan();", generatedCode, StringComparison.Ordinal);

        using var peStream = new MemoryStream();
        var emitResult = compilation.Emit(peStream);
        var diagnostics = string.Join('\n', emitResult.Diagnostics);
        Assert.True(emitResult.Success, diagnostics);

        var assembly = Assembly.Load(peStream.ToArray());
        var harnessType = assembly.GetType("Harness");
        var implementsMethod = harnessType?.GetMethod("ImplementsGenericInterface", BindingFlags.Public | BindingFlags.Static);
        var spanLengthMethod = harnessType?.GetMethod("GetUnsafeSpanLength", BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(implementsMethod);
        Assert.NotNull(spanLengthMethod);
        Assert.True((bool)implementsMethod!.Invoke(null, null)!);
        Assert.Equal(4, (int)spanLengthMethod!.Invoke(null, null)!);
    }

    [Fact]
    public async Task AnalyzerReportsMissingValue()
    {
        const string source = """
            [FixedStringBuilderAttribute]
            public partial struct Sample
            {
            }
            """;

        var diagnostics = await AnalyzeAsync(source);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("MFFSG0001", diagnostic.Id);
    }

    [Fact]
    public async Task AnalyzerReportsNonIntegerValue()
    {
        const string source = """
            [FixedStringBuilderAttribute("10")]
            public partial struct Sample
            {
            }
            """;

        var diagnostics = await AnalyzeAsync(source);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("MFFSG0002", diagnostic.Id);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task AnalyzerReportsNonPositiveValue(string length)
    {
        var source = $$"""
            [FixedStringBuilderAttribute({{length}})]
            public partial struct Sample
            {
            }
            """;

        var diagnostics = await AnalyzeAsync(source);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("MFFSG0003", diagnostic.Id);
    }

    private static async Task<(GeneratorDriverRunResult RunResult, Compilation Compilation)> GenerateAsync(string source)
    {
        var netcoreRef = await NuGetHelpers.GetNuGetReferences("Microsoft.NETCore.App.Ref", "8.0.0", "ref/net8.0/");
        var references = netcoreRef.Select(static location => MetadataReference.CreateFromFile(location)).ToArray();
        var compilation = CSharpCompilation.Create(
            "compilation",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        ISourceGenerator generator = new FixedStringBuilderSourceGenerator().AsSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create([generator], parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        Assert.Empty(diagnostics);
        return (driver.GetRunResult(), outputCompilation);
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var (_, compilation) = await GenerateAsync(source);
        var analyzer = new FixedStringBuilderAttributeAnalyzer();
        var diagnostics = await compilation
            .WithAnalyzers([analyzer])
            .GetAnalyzerDiagnosticsAsync();

        return [.. diagnostics
            .Where(static diagnostic => diagnostic.Id.StartsWith("MFFSG", StringComparison.Ordinal))
            .OrderBy(static diagnostic => diagnostic.Id, StringComparer.Ordinal)];
    }
}
