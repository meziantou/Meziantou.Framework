using UseIsEmptyAnalyzerType = Meziantou.Framework.Analyzers.FullPath.UseIsEmptyAnalyzer;
using UseIsEmptyCodeFixProviderType = Meziantou.Framework.Analyzers.FullPath.UseIsEmptyCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class UseIsEmptyRuleTests : FullPathAnalyzerTestBase
{
    [Theory]
    [InlineData("string.IsNullOrEmpty(fullPath)", "fullPath.IsEmpty")]
    [InlineData("string.IsNullOrWhiteSpace(fullPath)", "fullPath.IsEmpty")]
    [InlineData("fullPath == FullPath.Empty", "fullPath.IsEmpty")]
    [InlineData("fullPath != FullPath.Empty", "!fullPath.IsEmpty")]
    [InlineData("FullPath.Empty == fullPath", "fullPath.IsEmpty")]
    [InlineData("fullPath.Value.Length == 0", "fullPath.IsEmpty")]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForEmptinessCheck(string expression, string fixedExpression)
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

        var fixedSource = $$"""
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static bool M(FullPath fullPath)
                    {
                        return {{fixedExpression}};
                    }
                }
            }
            """;

        await CreateCodeFixTest<UseIsEmptyAnalyzerType, UseIsEmptyCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
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
