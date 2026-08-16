using UseIsEmptyAnalyzerType = Meziantou.Framework.Analyzers.FullPath.UseIsEmptyAnalyzer;

namespace Meziantou.Framework.Tests;

public sealed class UseIsEmptyRuleTests : FullPathAnalyzerTestBase
{
    [Theory]
    [InlineData("string.IsNullOrEmpty(fullPath)")]
    [InlineData("string.IsNullOrWhiteSpace(fullPath)")]
    [InlineData("fullPath == FullPath.Empty")]
    [InlineData("fullPath != FullPath.Empty")]
    [InlineData("FullPath.Empty == fullPath")]
    [InlineData("fullPath.Value.Length == 0")]
    public async Task Analyzer_ReportDiagnostic_ForEmptinessCheck(string expression)
    {
        var source = $$"""
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static bool M(FullPath fullPath)
                    {
                        return {|MFFP0021:{{expression}}|};
                    }
                }
            }
            """;

        await CreateAnalyzerTest<UseIsEmptyAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForIsEmpty()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static bool M(FullPath fullPath)
                    {
                        return fullPath.IsEmpty;
                    }
                }
            }
            """;

        await CreateAnalyzerTest<UseIsEmptyAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForStringEmptinessCheck()
    {
        var source = """
            namespace Sample
            {
                public static class TestClass
                {
                    public static bool M(string text)
                    {
                        return string.IsNullOrEmpty(text);
                    }
                }
            }
            """;

        await CreateAnalyzerTest<UseIsEmptyAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForComparisonBetweenTwoFullPaths()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static bool M(FullPath fullPath, FullPath other)
                    {
                        return fullPath == other;
                    }
                }
            }
            """;

        await CreateAnalyzerTest<UseIsEmptyAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
