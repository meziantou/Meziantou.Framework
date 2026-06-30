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
}
