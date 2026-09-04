#pragma warning disable MA0101 // String contains an implicit end of line character
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using TestUtilities;

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
        Assert.HasCount(3, runResult.GeneratedTrees);
        var generatedTree = Assert.Single(runResult.GeneratedTrees, static tree => tree.FilePath.Contains("FastEnumExtensions.", StringComparison.Ordinal));
        Assert.Contains(runResult.GeneratedTrees, static tree => tree.FilePath.EndsWith("Microsoft.CodeAnalysis.EmbeddedAttribute.g.cs", StringComparison.Ordinal));
        Assert.Contains(runResult.GeneratedTrees, static tree => tree.FilePath.EndsWith("Meziantou.Framework.Annotations.FastEnumAttribute.g.cs", StringComparison.Ordinal));

        var generatedCode = (await generatedTree.GetRootAsync()).ToFullString();
        Assert.DoesNotContain("return useMetadata ? s_definedMetadataNames_", generatedCode);
        Assert.Contains("return s_names_", generatedCode);
        Assert.DoesNotContain("private static ulong ToUInt64_", generatedCode);
        Assert.DoesNotContain("private static global::Sample.Color FromUInt64_", generatedCode);
        Assert.DoesNotContain("s_parseTokenIsMetadata_", generatedCode);
        Assert.DoesNotContain("s_definedNames_", generatedCode);
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
        Assert.Contains("namespace Sample.Generated", generatedCode);
        Assert.Contains("ToStringFast(this global::Sample.Color value, bool useMetadata)", generatedCode);
        Assert.Contains("HasFlagFast(this global::Sample.Color instance, global::Sample.Color flag)", generatedCode);
        Assert.Contains("static string? GetName(this global::Sample.Color instance)", generatedCode);
        Assert.Contains("\"Blue metadata\"", generatedCode);
        Assert.Contains("\"Red metadata\"", generatedCode);
        Assert.Contains("\"Green metadata\"", generatedCode);
        Assert.Contains("return (instance & flag) == flag;", generatedCode);
        Assert.Contains("var separatorIndex = global::System.MemoryExtensions.IndexOf(remaining, ',');", generatedCode);
        Assert.Contains("TryParseSingleMetadata_", generatedCode);
        Assert.Contains("IsNumericToken_", generatedCode);
        Assert.Contains("FormatFlagsName_", generatedCode);
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
        Assert.Contains("extension(global::Sample.Color)", generatedCode);
        Assert.Contains("static global::Sample.Color Parse(string value, bool ignoreCase)", generatedCode);
        Assert.Contains("static global::System.ReadOnlySpan<string> GetNames(bool useMetadata)", generatedCode);
        Assert.Contains("static global::System.ReadOnlySpan<global::Sample.Color> GetValues()", generatedCode);
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
        Assert.DoesNotContain("extension(global::Sample.Color)", generatedCode);
        Assert.DoesNotContain("GetNames(bool useMetadata)", generatedCode);
        Assert.DoesNotContain("GetValues()", generatedCode);
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
        Assert.Contains("string", diagnostic.GetMessage());
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
        Assert.Contains("(null)", diagnostic.GetMessage());
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
        { "_ = Enum.IsDefined(Sample.Color.Blue);", "MFEG0007", "global::Sample.Color.IsDefinedFast(Sample.Color.Blue)" },
        { "_ = Sample.Color.Blue.ToString();", "MFEG0008", "Sample.Color.Blue.ToStringFast()" },
    };

    [Theory]
    [MemberData(nameof(CodeFixCases))]
    public async Task CodeFixRewritesFastEnumApis(string invocation, string expectedDiagnosticId, string expectedInvocation)
    {
        var sourceCode = CreateCodeFixSource(invocation);
        var result = await ApplyCodeFixAsync(sourceCode, expectedDiagnosticId);
        Assert.Contains(expectedInvocation, result.FixedSource);
        Assert.Empty(result.CompilationErrors);
    }

    [Fact]
    public async Task AnalyzerDoesNotReportUnrelatedSystemEnumApis()
    {
        // FastEnumMethodKind.Parse used to be the enum's default value, so every unrecognized
        // System.Enum method was classified as Enum.Parse.
        var sourceCode = CreateAnalyzerSource("""
                        _ = Enum.GetUnderlyingType(typeof(Sample.Color));
                        _ = Enum.Format(typeof(Sample.Color), Sample.Color.Blue, "G");
                        _ = Enum.ToObject(typeof(Sample.Color), 1);
                        _ = Enum.GetValuesAsUnderlyingType(typeof(Sample.Color));
            """, useFastEnumAttribute: true);

        var diagnostics = await AnalyzeFiles(sourceCode);
        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("_ = Enum.Parse<Sample.Color>(\"Blue\", false);")]
    [InlineData("_ = Enum.TryParse<Sample.Color>(\"Blue\", out var parsed);")]
    [InlineData("_ = Enum.GetNames<Sample.Color>();")]
    [InlineData("_ = Enum.GetValues<Sample.Color>();")]
    [InlineData("_ = Enum.IsDefined(Sample.Color.Blue);")]
    public async Task AnalyzerDoesNotReportExtensionMemberRulesBelowCSharp14(string invocation)
    {
        // Those members are only generated when the compilation supports extension members.
        var sourceCode = CreateAnalyzerSource(invocation, useFastEnumAttribute: true);
        var diagnostics = await AnalyzeFiles(sourceCode, LanguageVersion.CSharp12);
        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("_ = Enum.GetName(Sample.Color.Blue);", "MFEG0006")]
    [InlineData("_ = Sample.Color.Blue.ToString();", "MFEG0008")]
    public async Task AnalyzerStillReportsInstanceExtensionRulesBelowCSharp14(string invocation, string expectedDiagnosticId)
    {
        var sourceCode = CreateAnalyzerSource(invocation, useFastEnumAttribute: true);
        var diagnostics = await AnalyzeFiles(sourceCode, LanguageVersion.CSharp12);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(expectedDiagnosticId, diagnostic.Id);
    }

    [Fact]
    public async Task AnalyzerReportsEnumWithoutMembers()
    {
        var sourceCode = """
            [assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Sample.Empty))]
            namespace Sample
            {
                public enum Empty
                {
                }
            }
            """;

        var diagnostics = await AnalyzeFiles(sourceCode);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("MFEG0009", diagnostic.Id);
    }

    [Fact]
    public async Task AnalyzerDoesNotSuggestMembersForEnumWithoutMembers()
    {
        // Nothing is generated for an empty enum, so the invocation rules must stay quiet.
        var sourceCode = """
            using System;
            [assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Sample.Empty))]
            namespace Sample
            {
                public enum Empty
                {
                }

                public static class TestClass
                {
                    public static void M()
                    {
                        _ = default(Empty).ToString();
                    }
                }
            }
            """;

        var diagnostics = await AnalyzeFiles(sourceCode);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("MFEG0009", diagnostic.Id);
    }

    [Fact]
    public async Task DuplicateFastEnumAttributeGeneratesASingleClass()
    {
        // Emitting the enum twice makes every call site ambiguous (CS0121).
        var sourceCode = """
            [assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Sample.Color))]
            [assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Sample.Color))]
            namespace Sample
            {
                public enum Color
                {
                    Blue,
                    Red,
                }

                public static class TestClass
                {
                    public static string M() => Color.Blue.ToStringFast();
                }
            }
            """;

        var (runResult, _) = await GenerateFiles(sourceCode);
        var generatedTrees = runResult.GeneratedTrees.Where(static tree => tree.FilePath.Contains("FastEnumExtensions.", StringComparison.Ordinal)).ToArray();
        _ = Assert.Single(generatedTrees);
    }

    [Fact]
    public async Task KeywordNamedMembersGenerateCompilableCode()
    {
        var sourceCode = """
            [assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Sample.Keywords))]
            namespace Sample
            {
                public enum Keywords
                {
                    @class,
                    @new,
                    @event,
                }
            }
            """;

        // GenerateFiles asserts the resulting compilation emits successfully.
        var generatedCode = await GenerateCode(sourceCode);
        Assert.Contains("global::Sample.Keywords.@class", generatedCode);
    }

    [Fact]
    public async Task DoesNotEmitSpanApisOrUnusedArraysBelowCSharp14()
    {
        // A netstandard2.0 consumer has no ReadOnlySpan<char>, and the parse tables are unused there.
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
        Assert.DoesNotContain("ReadOnlySpan", generatedCode);
        Assert.DoesNotContain("s_parseTokens_", generatedCode);
        Assert.DoesNotContain("s_values_", generatedCode);
    }

    [Fact]
    public async Task GeneratedClassNameIsStableWhenAnotherEnumIsAdded()
    {
        // A positional suffix would rename the class and break assemblies already compiled against it.
        var singleEnum = """
            [assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Sample.Color))]
            namespace Sample
            {
                public enum Color { Blue, Red }
            }
            """;
        var twoEnums = """
            [assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Sample.Color))]
            [assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Sample.Alpha))]
            namespace Sample
            {
                public enum Color { Blue, Red }
                public enum Alpha { X, Y }
            }
            """;

        var before = GetGeneratedClassName(await GenerateCodeForEnum(singleEnum, "Color"));
        var after = GetGeneratedClassName(await GenerateCodeForEnum(twoEnums, "Color"));
        Assert.Equal(before, after);

        static string GetGeneratedClassName(string generatedCode)
        {
            const string Marker = "static class ";
            var start = generatedCode.IndexOf(Marker, StringComparison.Ordinal);
            Assert.True(start >= 0, "The generated class name should be present.");
            start += Marker.Length;
            var end = generatedCode.IndexOfAny(['\r', '\n', ' '], start);
            return end < 0 ? generatedCode[start..] : generatedCode[start..end];
        }
    }

    [Theory]
    [InlineData("public", null, "public static class")]
    [InlineData("public", "IsPublic = false", "internal static class")]
    [InlineData("internal", null, "internal static class")]
    [InlineData("internal", "IsPublic = true", "internal static class")]
    public async Task IsPublicControlsGeneratedVisibility(string enumVisibility, string? attributeArgument, string expectedDeclaration)
    {
        var arguments = attributeArgument is null ? "" : ", " + attributeArgument;
        var sourceCode = $$"""
            [assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Sample.Color){{arguments}})]
            namespace Sample
            {
                {{enumVisibility}} enum Color { Blue, Red }
            }
            """;

        var generatedCode = await GenerateCode(sourceCode);
        Assert.Contains(expectedDeclaration, generatedCode);
    }

    [Fact]
    public async Task VisibilityIsComputedPerEnumNotPerNamespace()
    {
        // A public enum in the namespace must not make the internal enum's class public.
        var sourceCode = """
            [assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Sample.PublicColor))]
            [assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Sample.InternalColor))]
            namespace Sample
            {
                public enum PublicColor { Blue, Red }
                internal enum InternalColor { Cyan, Magenta }
            }
            """;

        var internalCode = await GenerateCodeForEnum(sourceCode, "InternalColor");
        Assert.Contains("internal static class", internalCode);
        Assert.DoesNotContain("public static class", internalCode);
    }

    [Theory]
    [InlineData("string[] names = Enum.GetNames<Sample.Color>();", "MFEG0004")]
    [InlineData("Sample.Color[] values = Enum.GetValues<Sample.Color>();", "MFEG0005")]
    [InlineData("System.Collections.Generic.IEnumerable<string> names = Enum.GetNames<Sample.Color>();", "MFEG0004")]
    public async Task CodeFixIsNotOfferedWhenSpanResultIsNotAssignable(string invocation, string diagnosticId)
    {
        // The generated members return ReadOnlySpan<T>, which does not convert to an array or IEnumerable.
        var sourceCode = CreateCodeFixSource(invocation);
        Assert.False(await IsCodeFixOfferedAsync(sourceCode, diagnosticId));
    }

    [Theory]
    [InlineData("foreach (var name in Enum.GetNames<Sample.Color>()) { _ = name; }", "MFEG0004")]
    [InlineData("_ = Enum.GetNames<Sample.Color>().Length;", "MFEG0004")]
    public async Task CodeFixIsOfferedWhenSpanResultIsUsable(string invocation, string diagnosticId)
    {
        var sourceCode = CreateCodeFixSource(invocation);
        var result = await ApplyCodeFixAsync(sourceCode, diagnosticId);
        Assert.Empty(result.CompilationErrors);
    }

    [Fact]
    public async Task CodeFixAddsUsingForExtensionMethodNamespace()
    {
        var sourceCode = CreateCodeFixSource("_ = Sample.Color.Blue.ToString();", extensionMethodNamespace: "Sample.Extensions");
        var result = await ApplyCodeFixAsync(sourceCode, "MFEG0008");
        Assert.Contains("using Sample.Extensions;", result.FixedSource);
        Assert.Empty(result.CompilationErrors);
    }

    [Theory]
    [InlineData("_ = Enum.IsDefined(typeof(Sample.Color), \"Blue\");")]
    [InlineData("object boxed = 1; _ = Enum.IsDefined(typeof(Sample.Color), boxed);")]
    public async Task CodeFixIsNotOfferedForIsDefinedWithNonEnumArgument(string invocation)
    {
        // Enum.IsDefined(Type, object) also accepts a member name or a boxed underlying value.
        var sourceCode = CreateCodeFixSource(invocation);
        Assert.False(await IsCodeFixOfferedAsync(sourceCode, "MFEG0007"));
    }

    [Fact]
    public async Task CodeFixParenthesizesTheReceiverForGetName()
    {
        var sourceCode = CreateCodeFixSource("_ = Enum.GetName(Sample.Color.Blue | Sample.Color.Red);");
        var result = await ApplyCodeFixAsync(sourceCode, "MFEG0006");
        Assert.Contains("(Sample.Color.Blue | Sample.Color.Red).GetName()", result.FixedSource);
        Assert.Empty(result.CompilationErrors);
    }

    private static async Task<string> GenerateCodeForEnum(string sourceCode, string enumName)
    {
        var (runResult, _) = await GenerateFiles(sourceCode);
        var generatedTree = Assert.Single(runResult.GeneratedTrees, tree => tree.FilePath.Contains("FastEnumExtensions.Sample_" + enumName + "_", StringComparison.Ordinal));
        return (await generatedTree.GetRootAsync()).ToFullString();
    }

    private const string InterceptorsNamespace = "Meziantou.Framework.Annotations.FastEnumInterceptors";

    private const string InterceptorSource = """
        using System;
        [assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Sample.Color))]
        namespace Sample
        {
            [Flags]
            public enum Color { None = 0, Blue = 1, Red = 2 }

            public static class TestClass
            {
                public static void M()
                {
                    _ = Color.Blue.ToString();
                    _ = Color.Blue.HasFlag(Color.Red);
                    _ = Enum.IsDefined(Color.Blue);
                    _ = Enum.GetName(Color.Blue);
                    _ = Enum.GetNames<Color>();
                    _ = Enum.GetValues<Color>();
                }
            }
        }
        """;

    [Fact]
    public async Task DoesNotGenerateInterceptorsByDefault()
    {
        // Interceptors rewrite call sites silently, so they must stay opt-in.
        var (runResult, _) = await GenerateFiles(InterceptorSource);
        Assert.DoesNotContain(runResult.GeneratedTrees, static tree => tree.FilePath.Contains("FastEnumInterceptors", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GeneratesInterceptorsForEverySupportedCallWhenEnabled()
    {
        var (runResult, _) = await GenerateFiles(InterceptorSource, interceptorsEnabled: true);
        var tree = Assert.Single(runResult.GeneratedTrees, static tree => tree.FilePath.Contains("FastEnumInterceptors", StringComparison.Ordinal));
        var generatedCode = (await tree.GetRootAsync()).ToFullString();

        Assert.Contains("namespace " + InterceptorsNamespace, generatedCode);
        Assert.Contains("InterceptsLocationAttribute(1,", generatedCode);

        // ToString and HasFlag are declared on System.Enum, so their interceptors must take that receiver.
        Assert.Contains("public static string ToStringFast(this global::System.Enum value)", generatedCode);
        Assert.Contains("public static bool HasFlagFast(this global::System.Enum value, global::System.Enum flag)", generatedCode);

        // The static Enum members are intercepted by enum-specialized, non-generic methods.
        Assert.Contains("public static bool IsDefinedFast(global::Sample.Color value)", generatedCode);
        Assert.Contains("public static string? GetNameFast(global::Sample.Color value)", generatedCode);
        Assert.Contains("public static string[] GetNamesFast()", generatedCode);
        Assert.Contains("public static global::Sample.Color[] GetValuesFast()", generatedCode);

        // Enum.GetNames/GetValues return a caller-owned array.
        Assert.Contains(".Clone()", (await Assert.Single(runResult.GeneratedTrees, static tree => tree.FilePath.Contains("FastEnumExtensions.", StringComparison.Ordinal)).GetRootAsync()).ToFullString());
    }

    [Fact]
    public async Task DoesNotGenerateInterceptorsBelowCSharp12()
    {
        // InterceptsLocationAttribute requires C# 12.
        var (runResult, _) = await GenerateFiles(InterceptorSource, LanguageVersion.CSharp11, interceptorsEnabled: true);
        Assert.DoesNotContain(runResult.GeneratedTrees, static tree => tree.FilePath.Contains("FastEnumInterceptors", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DoesNotGenerateInterceptorsForEnumsWithoutTheAttribute()
    {
        var sourceCode = """
            using System;
            namespace Sample
            {
                public enum Color { Blue, Red }

                public static class TestClass
                {
                    public static void M() => Console.WriteLine(Color.Blue.ToString());
                }
            }
            """;

        var (runResult, _) = await GenerateFiles(sourceCode, interceptorsEnabled: true);
        Assert.DoesNotContain(runResult.GeneratedTrees, static tree => tree.FilePath.Contains("FastEnumInterceptors", StringComparison.Ordinal));
    }

    private sealed class TestAnalyzerConfigOptionsProvider(Dictionary<string, string> globalOptions) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new TestAnalyzerConfigOptions(globalOptions);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => TestAnalyzerConfigOptions.Empty;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => TestAnalyzerConfigOptions.Empty;
    }

    private sealed class TestAnalyzerConfigOptions(Dictionary<string, string> values) : AnalyzerConfigOptions
    {
        public static readonly TestAnalyzerConfigOptions Empty = new(new Dictionary<string, string>(StringComparer.Ordinal));

        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value) => values.TryGetValue(key, out value);
    }

    private static async Task<string> GenerateCode(string sourceCode, LanguageVersion languageVersion = LanguageVersion.Preview)
    {
        var (runResult, _) = await GenerateFiles(sourceCode, languageVersion);
        var generatedTree = Assert.Single(runResult.GeneratedTrees, static tree => tree.FilePath.Contains("FastEnumExtensions.", StringComparison.Ordinal));
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

    private static async Task<(GeneratorDriverRunResult RunResult, Compilation Compilation)> GenerateFiles(string sourceCode, LanguageVersion languageVersion = LanguageVersion.Preview, bool interceptorsEnabled = false)
    {
        var netcoreReferences = await NuGetHelpers.GetNuGetReferences("Microsoft.NETCore.App.Ref", "8.0.0", "ref/net8.0/");
        var references = netcoreReferences.Select(static location => MetadataReference.CreateFromFile(location)).ToArray();
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(languageVersion);
        if (interceptorsEnabled)
        {
            // Mirrors what the package's .targets sets through InterceptorsNamespaces.
            parseOptions = parseOptions.WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", InterceptorsNamespace)]);
        }
        var compilation = CSharpCompilation.Create(
            "compilation",
            [CSharpSyntaxTree.ParseText(sourceCode, parseOptions)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new FastEnumSourceGenerator().AsSourceGenerator();
        var optionsProvider = new TestAnalyzerConfigOptionsProvider(interceptorsEnabled
            ? new Dictionary<string, string>(StringComparer.Ordinal) { ["build_property.MeziantouFastEnumInterceptors"] = "true" }
            : new Dictionary<string, string>(StringComparer.Ordinal));
        GeneratorDriver driver = CSharpGeneratorDriver.Create([generator], parseOptions: parseOptions, optionsProvider: optionsProvider);
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

    private static string CreateCodeFixSource(string invocation, string? extensionMethodNamespace = null)
    {
        var attributeArguments = extensionMethodNamespace is null ? "" : $", ExtensionMethodNamespace = \"{extensionMethodNamespace}\"";
        return $$"""
            using System;

            [assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Sample.Color){{attributeArguments}})]

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

    private sealed record CodeFixResult(string FixedSource, ImmutableArray<Diagnostic> CompilationErrors);

    /// <summary>
    /// Builds a workspace containing the source plus the generator's output, so a fix is verified against
    /// the members that actually exist and the result can be compiled.
    /// </summary>
    private static async Task<(Solution Solution, ProjectId ProjectId, DocumentId DocumentId, AdhocWorkspace Workspace)> CreateWorkspaceWithGeneratedCodeAsync(string sourceCode, LanguageVersion languageVersion)
    {
        var (runResult, _) = await GenerateFiles(sourceCode, languageVersion);

        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);
        var netcoreReferences = await NuGetHelpers.GetNuGetReferences("Microsoft.NETCore.App.Ref", "8.0.0", "ref/net8.0/");

        var solution = workspace.CurrentSolution
            .AddProject(projectId, "Project", "Project", LanguageNames.CSharp)
            .WithProjectParseOptions(projectId, CSharpParseOptions.Default.WithLanguageVersion(languageVersion))
            .WithProjectCompilationOptions(projectId, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable))
            .AddDocument(documentId, "Test.cs", SourceText.From(sourceCode, Encoding.UTF8));

        foreach (var location in netcoreReferences)
        {
            solution = solution.AddMetadataReference(projectId, MetadataReference.CreateFromFile(location));
        }

        var index = 0;
        foreach (var generatedTree in runResult.GeneratedTrees)
        {
            solution = solution.AddDocument(DocumentId.CreateNewId(projectId), $"Generated{index++}.cs", SourceText.From((await generatedTree.GetRootAsync()).ToFullString(), Encoding.UTF8));
        }

        return (solution, projectId, documentId, workspace);
    }

    private static async Task<List<CodeAction>> GetCodeActionsAsync(Solution solution, ProjectId projectId, DocumentId documentId, string diagnosticId)
    {
        var project = solution.GetProject(projectId) ?? throw new InvalidOperationException("Project should be available.");
        var document = project.GetDocument(documentId) ?? throw new InvalidOperationException("Document should be available.");
        var compilation = await project.GetCompilationAsync() ?? throw new InvalidOperationException("Compilation should be available.");

        var diagnostics = await compilation
            .WithAnalyzers([new FastEnumAnalyzer()])
            .GetAnalyzerDiagnosticsAsync();

        var syntaxTree = await document.GetSyntaxTreeAsync();
        var diagnostic = Assert.Single(diagnostics, diag => diag.Id == diagnosticId && diag.Location.SourceTree == syntaxTree);
        var codeActions = new List<CodeAction>();
        var codeFixContext = new CodeFixContext(document, diagnostic, (action, _) => codeActions.Add(action), CancellationToken.None);
        await new FastEnumCodeFixProvider().RegisterCodeFixesAsync(codeFixContext);
        return codeActions;
    }

    private static async Task<CodeFixResult> ApplyCodeFixAsync(string sourceCode, string diagnosticId, LanguageVersion languageVersion = LanguageVersion.Preview)
    {
        var (solution, projectId, documentId, workspace) = await CreateWorkspaceWithGeneratedCodeAsync(sourceCode, languageVersion);
        using (workspace)
        {
            var codeActions = await GetCodeActionsAsync(solution, projectId, documentId, diagnosticId);
            var codeAction = Assert.Single(codeActions);
            var operation = Assert.Single((await codeAction.GetOperationsAsync(CancellationToken.None)).OfType<ApplyChangesOperation>());
            var changedDocument = operation.ChangedSolution.GetDocument(documentId) ?? throw new InvalidOperationException("Changed document should be available.");
            var changedProject = changedDocument.Project;
            var changedCompilation = await changedProject.GetCompilationAsync() ?? throw new InvalidOperationException("Compilation should be available.");

            var errors = changedCompilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToImmutableArray();
            return new CodeFixResult((await changedDocument.GetTextAsync()).ToString(), errors);
        }
    }

    private static async Task<bool> IsCodeFixOfferedAsync(string sourceCode, string diagnosticId, LanguageVersion languageVersion = LanguageVersion.Preview)
    {
        var (solution, projectId, documentId, workspace) = await CreateWorkspaceWithGeneratedCodeAsync(sourceCode, languageVersion);
        using (workspace)
        {
            return (await GetCodeActionsAsync(solution, projectId, documentId, diagnosticId)).Count > 0;
        }
    }
}
