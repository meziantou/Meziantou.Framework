using CompareFullPathAsStringAnalyzerType = Meziantou.Framework.Analyzers.FullPath.CompareFullPathAsStringAnalyzer;

namespace Meziantou.Framework.Tests;

public sealed class CompareFullPathAsStringRuleTests : FullPathAnalyzerTestBase
{
    [Theory]
    [InlineData("fullPath == \"value\"")]
    [InlineData("fullPath != \"value\"")]
    [InlineData("fullPath.Value == other.Value")]
    [InlineData("fullPath.ToString() == text")]
    [InlineData("text == fullPath")]
    public async Task Analyzer_ReportDiagnostic_ForStringComparison(string expression)
    {
        var source = $$"""
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static bool M(FullPath fullPath, FullPath other, string text)
                    {
                        return {|MFFP0015:{{expression.TrimEnd()}}|};
                    }
                }
            }
            """;

        await CreateAnalyzerTest<CompareFullPathAsStringAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_ForStringEquals()
    {
        var source = """
            using System;
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static bool M(FullPath fullPath, string text)
                    {
                        return {|MFFP0015:string.Equals(fullPath, text)|};
                    }
                }
            }
            """;

        await CreateAnalyzerTest<CompareFullPathAsStringAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForFullPathComparison()
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

        await CreateAnalyzerTest<CompareFullPathAsStringAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForStringComparisonBetweenStrings()
    {
        var source = """
            namespace Sample
            {
                public static class TestClass
                {
                    public static bool M(string text1, string text2)
                    {
                        return text1 == text2;
                    }
                }
            }
            """;

        await CreateAnalyzerTest<CompareFullPathAsStringAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_WhenStringComparisonIsExplicit()
    {
        var source = """
            using System;
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static bool M(FullPath fullPath, string text)
                    {
                        return string.Equals(fullPath, text, StringComparison.Ordinal);
                    }
                }
            }
            """;

        await CreateAnalyzerTest<CompareFullPathAsStringAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
