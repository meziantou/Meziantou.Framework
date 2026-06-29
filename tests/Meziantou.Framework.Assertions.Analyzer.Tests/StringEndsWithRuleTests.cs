using StringEndsWithAnalyzerType = Meziantou.Framework.Analyzers.Assertions.StringEndsWithAnalyzer;
using StringEndsWithCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.StringEndsWithCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class StringEndsWithRuleTests : AssertionsAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertTrueStringEndsWith()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.True({|MFAS0020:value.EndsWith("abc")|});
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.EndsWith("abc", value);
                }
            }
            """;

        await CreateCodeFixTest<StringEndsWithAnalyzerType, StringEndsWithCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertFalseStringEndsWith()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.False({|MFAS0021:value.EndsWith("abc")|});
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.DoesNotEndWith("abc", value);
                }
            }
            """;

        await CreateCodeFixTest<StringEndsWithAnalyzerType, StringEndsWithCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }
}
