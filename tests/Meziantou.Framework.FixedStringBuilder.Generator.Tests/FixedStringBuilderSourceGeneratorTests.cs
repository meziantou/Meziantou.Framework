using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using TestUtilities;

namespace Meziantou.Framework.FixedStringBuilder.Generator.Tests;

public sealed class FixedStringBuilderSourceGeneratorTests
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Preview);

    [Fact]
    public async Task GeneratesFixedStringFromAttribute()
    {
        const string Source = """
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

        var (runResult, compilation) = await GenerateAsync(Source);
        Assert.Empty(runResult.Diagnostics);
        Assert.Single(runResult.Results);
        Assert.HasCount(3, runResult.Results[0].GeneratedSources);

        var allGeneratedSources = string.Join('\n', runResult.Results[0].GeneratedSources.Select(static source => source.SourceText.ToString()));
        Assert.Contains("internal partial class FixedStringBuilderAttribute", allGeneratedSources);
        Assert.Contains("internal sealed partial class EmbeddedAttribute", allGeneratedSources);
        Assert.Contains("public static int MaxLength => 10;", allGeneratedSources);
        Assert.Contains("[global::System.Runtime.CompilerServices.InlineArray(10)]", allGeneratedSources);
        Assert.Contains("private Storage _storage;", allGeneratedSources);
        Assert.Contains("public readonly ReadOnlySpan<char> AsSpan() => ((ReadOnlySpan<char>)_storage).Slice(0, _length);", allGeneratedSources);
        Assert.Contains("public readonly bool Equals(FixedStringBuilder10 other, StringComparison comparison)", allGeneratedSources);

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
        const string Source = """
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

        var (runResult, compilation) = await GenerateAsync(Source);
        Assert.Empty(runResult.Diagnostics);

        var generatedCode = string.Join('\n', runResult.Results[0].GeneratedSources.Select(static source => source.SourceText.ToString()));
        Assert.Contains("global::Meziantou.Framework.FixedStringBuilder.IFixedString<global::FixedStringBuilder4>", generatedCode);
        Assert.Contains("global::Meziantou.Framework.FixedStringBuilder.IFixedString.GetUnsafeFullSpan() => AsUnsafeFullSpan();", generatedCode);

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
    public async Task ClearCanZeroTheBuffer()
    {
        const string Source = """
            namespace Meziantou.Framework.FixedStringBuilder
            {
                public interface IFixedString
                {
                    global::System.Span<char> GetUnsafeFullSpan();
                }

                public interface IFixedString<T> : IFixedString where T : IFixedString<T>
                {
                    void Clear();
                    void Clear(bool zeroBuffer);
                    static abstract implicit operator T(string value);
                }
            }

            [FixedStringBuilderAttribute(4)]
            public partial struct FixedStringBuilder4
            {
            }

            public static class Harness
            {
                public static string ClearAndGetBuffer(bool zeroBuffer)
                {
                    FixedStringBuilder4 value = "abcd";
                    value.Clear(zeroBuffer);
                    return new string(((Meziantou.Framework.FixedStringBuilder.IFixedString)value).GetUnsafeFullSpan());
                }
            }
            """;

        var (runResult, compilation) = await GenerateAsync(Source);
        Assert.Empty(runResult.Diagnostics);

        var generatedCode = string.Join('\n', runResult.Results[0].GeneratedSources.Select(static source => source.SourceText.ToString()));
        Assert.Contains("public void Clear(bool zeroBuffer)", generatedCode);
        Assert.Contains("AsUnsafeFullSpan().Clear();", generatedCode);

        using var peStream = new MemoryStream();
        var emitResult = compilation.Emit(peStream);
        var diagnostics = string.Join('\n', emitResult.Diagnostics);
        Assert.True(emitResult.Success, diagnostics);

        var assembly = Assembly.Load(peStream.ToArray());
        var method = assembly.GetType("Harness")?.GetMethod("ClearAndGetBuffer", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        Assert.Equal("abcd", (string?)method!.Invoke(null, [false]));
        Assert.Equal("\0\0\0\0", (string?)method!.Invoke(null, [true]));
    }

    [Fact]
    public async Task AnalyzerReportsMissingValue()
    {
        const string Source = """
            [FixedStringBuilderAttribute]
            public partial struct Sample
            {
            }
            """;

        var diagnostics = await AnalyzeAsync(Source);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("MFFSG0001", diagnostic.Id);
    }

    [Fact]
    public async Task AnalyzerReportsNonIntegerValue()
    {
        const string Source = """
            [FixedStringBuilderAttribute("10")]
            public partial struct Sample
            {
            }
            """;

        var diagnostics = await AnalyzeAsync(Source);
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

    [Fact]
    public async Task AnalyzerReportsLengthAboveMaximum()
    {
        const string Source = """
            [FixedStringBuilderAttribute(32768)]
            public partial struct Sample
            {
            }
            """;

        var diagnostics = await AnalyzeAsync(Source);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("MFFSG0004", diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotGenerateWhenLengthIsAboveMaximum()
    {
        const string Source = """
            [FixedStringBuilderAttribute(32768)]
            public partial struct Sample
            {
            }
            """;

        var (runResult, _) = await GenerateAsync(Source);

        // Only the two post-initialization sources: the type itself is not generated because its length would
        // overflow the short used to count the characters.
        Assert.HasCount(2, runResult.Results[0].GeneratedSources);
    }

    [Fact]
    public async Task AnalyzerReportsNonPartialType()
    {
        const string Source = """
            [FixedStringBuilderAttribute(4)]
            public struct Sample
            {
            }
            """;

        var diagnostics = await AnalyzeAsync(Source);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("MFFSG0005", diagnostic.Id);
    }

    [Theory]
    [InlineData("readonly partial")]
    [InlineData("ref partial")]
    public async Task AnalyzerReportsReadOnlyOrRefType(string modifiers)
    {
        var source = $$"""
            [FixedStringBuilderAttribute(4)]
            public {{modifiers}} struct Sample
            {
            }
            """;

        var diagnostics = await AnalyzeAsync(source);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("MFFSG0006", diagnostic.Id);
    }

    [Theory]
    [InlineData("public struct Sample { }")]
    [InlineData("public readonly partial struct Sample { }")]
    [InlineData("public ref partial struct Sample { }")]
    public async Task DoesNotGenerateForUnsupportedTypeShapes(string declaration)
    {
        var source = $$"""
            [FixedStringBuilderAttribute(4)]
            {{declaration}}
            """;

        var (runResult, _) = await GenerateAsync(source);

        // Only the two post-initialization sources: the members would not compile in these shapes.
        Assert.HasCount(2, runResult.Results[0].GeneratedSources);
    }

    [Fact]
    public async Task GeneratesBothTypesWhenTheirSanitizedNamesCollide()
    {
        // "A.B.C" and "A.B_C" both sanitize to "A_B_C"
        const string Source = """
            namespace A.B
            {
                [FixedStringBuilderAttribute(4)]
                public partial struct C;
            }

            namespace A
            {
                [FixedStringBuilderAttribute(4)]
                public partial struct B_C;
            }
            """;

        var (runResult, compilation) = await GenerateAsync(Source);
        Assert.Empty(runResult.Diagnostics);

        var hintNames = runResult.Results[0].GeneratedSources.Select(static source => source.HintName).ToArray();
        Assert.HasCount(4, hintNames);
        Assert.HasCount(hintNames.Length, hintNames.Distinct(StringComparer.Ordinal));

        using var peStream = new MemoryStream();
        var emitResult = compilation.Emit(peStream);
        Assert.True(emitResult.Success, string.Join('\n', emitResult.Diagnostics));
    }

    [Fact]
    public async Task OutputIsCachedWhenAnUnrelatedFileChanges()
    {
        const string Target = """
            [FixedStringBuilderAttribute(10)]
            public partial struct FixedStringBuilder10;
            """;

        var netcoreRef = await NuGetHelpers.GetNuGetReferences("Microsoft.NETCore.App.Ref", "10.0.0", "ref/net10.0/");
        var references = netcoreRef.Select(static location => MetadataReference.CreateFromFile(location)).ToArray();

        Compilation CreateCompilation(string unrelatedSource) => CSharpCompilation.Create(
            "compilation",
            [CSharpSyntaxTree.ParseText(Target, ParseOptions), CSharpSyntaxTree.ParseText(unrelatedSource, ParseOptions)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        ISourceGenerator generator = new FixedStringBuilderSourceGenerator().AsSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [generator],
            parseOptions: ParseOptions,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(CreateCompilation("internal sealed class Unrelated { }"));
        driver = driver.RunGenerators(CreateCompilation("internal sealed class Unrelated { private int _field; }"));

        var outputs = driver.GetRunResult().Results[0].TrackedSteps
            .Where(static step => step.Key.StartsWith("SourceOutput", StringComparison.Ordinal))
            .SelectMany(static step => step.Value)
            .SelectMany(static step => step.Outputs)
            .ToArray();

        Assert.NotEmpty(outputs);
        Assert.All(outputs, static output => Assert.Equal(IncrementalStepRunReason.Cached, output.Reason));
    }

    [Fact]
    public async Task AsSpanCannotOutliveTheValueItPointsInto()
    {
        const string Source = """
            [FixedStringBuilderAttribute(10)]
            public partial struct FixedStringBuilder10
            {
            }

            public static class Harness
            {
                public static System.ReadOnlySpan<char> Escape()
                {
                    FixedStringBuilder10 value = "abcd";
                    return value.AsSpan();
                }
            }
            """;

        var (_, compilation) = await GenerateAsync(Source);

        var errors = compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        var error = Assert.Single(errors);
        Assert.Equal("CS8168", error.Id);
    }

    [Fact]
    public async Task AnalyzerIgnoresAnUnrelatedAttributeThatDoesNotBind()
    {
        // Other.FixedStringBuilderAttribute has no parameterless constructor, so the attribute does not bind to a
        // symbol. It is still not the generator's attribute and must not be reported on.
        const string Source = """
            namespace Other
            {
                [System.AttributeUsage(System.AttributeTargets.Struct)]
                public sealed class FixedStringBuilderAttribute : System.Attribute
                {
                    public FixedStringBuilderAttribute(string name) { }
                }
            }

            [Other.FixedStringBuilder]
            public partial struct Sample
            {
            }
            """;

        var diagnostics = await AnalyzeAsync(Source);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AnalyzerReportsThroughAnAlias()
    {
        // The attribute is the generator's one, so it must be analyzed even though the name is not written out.
        const string Source = """
            using Aliased = FixedStringBuilderAttribute;

            [Aliased(0)]
            public partial struct Sample
            {
            }
            """;

        var diagnostics = await AnalyzeAsync(Source);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("MFFSG0003", diagnostic.Id);
    }

    private static async Task<(GeneratorDriverRunResult RunResult, Compilation Compilation)> GenerateAsync(string source)
    {
        var netcoreRef = await NuGetHelpers.GetNuGetReferences("Microsoft.NETCore.App.Ref", "10.0.0", "ref/net10.0/");
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
