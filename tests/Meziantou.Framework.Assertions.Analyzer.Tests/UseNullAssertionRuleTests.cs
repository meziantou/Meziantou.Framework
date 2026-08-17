using UseNullAssertionAnalyzerType = Meziantou.Framework.Analyzers.Assertions.UseNullAssertionAnalyzer;
using UseNullAssertionCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.UseNullAssertionCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class UseNullAssertionRuleTests : AssertionsAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertTrueEqualsNull()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string? value)
                {
                    Assert.True({|MFAS0006:value|} == null);
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string? value)
                {
                    Assert.Null(value);
                }
            }
            """;

        await CreateCodeFixTest<UseNullAssertionAnalyzerType, UseNullAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertTrueNotEqualsNull()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string? value)
                {
                    Assert.True({|MFAS0007:value|} != null);
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string? value)
                {
                    Assert.NotNull(value);
                }
            }
            """;

        await CreateCodeFixTest<UseNullAssertionAnalyzerType, UseNullAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertTrueIsNull()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string? value)
                {
                    Assert.True({|MFAS0006:value|} is null);
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string? value)
                {
                    Assert.Null(value);
                }
            }
            """;

        await CreateCodeFixTest<UseNullAssertionAnalyzerType, UseNullAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertTrueIsNotNull()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string? value)
                {
                    Assert.True({|MFAS0007:value|} is not null);
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string? value)
                {
                    Assert.NotNull(value);
                }
            }
            """;

        await CreateCodeFixTest<UseNullAssertionAnalyzerType, UseNullAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Theory]
    [InlineData("Assert.False({|MFAS0007:value|} == null);", "Assert.NotNull(value);")]
    [InlineData("Assert.False({|MFAS0006:value|} != null);", "Assert.Null(value);")]
    [InlineData("Assert.False({|MFAS0007:value|} is null);", "Assert.NotNull(value);")]
    [InlineData("Assert.False({|MFAS0006:value|} is not null);", "Assert.Null(value);")]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertFalseNullCheck(string assertion, string fixedAssertion)
    {
        var source = $$"""
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string? value)
                {
                    {{assertion}}
                }
            }
            """;

        var fixedSource = $$"""
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string? value)
                {
                    {{fixedAssertion}}
                }
            }
            """;

        await CreateCodeFixTest<UseNullAssertionAnalyzerType, UseNullAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Theory]
    [InlineData("Assert.True({|MFAS0007:value|}.HasValue);", "Assert.NotNull(value);")]
    [InlineData("Assert.False({|MFAS0006:value|}.HasValue);", "Assert.Null(value);")]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForNullableHasValue(string assertion, string fixedAssertion)
    {
        var source = $$"""
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int? value)
                {
                    {{assertion}}
                }
            }
            """;

        var fixedSource = $$"""
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int? value)
                {
                    {{fixedAssertion}}
                }
            }
            """;

        await CreateCodeFixTest<UseNullAssertionAnalyzerType, UseNullAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForHasValueOnCustomType()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public sealed class Option
            {
                public bool HasValue => true;
            }

            public static class TestClass
            {
                public static void M(Option value)
                {
                    Assert.True(value.HasValue);
                    Assert.False(value.HasValue);
                }
            }
            """;

        await CreateAnalyzerTest<UseNullAssertionAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
