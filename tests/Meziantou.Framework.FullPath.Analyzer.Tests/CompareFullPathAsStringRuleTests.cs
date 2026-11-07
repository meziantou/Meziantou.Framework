using Microsoft.CodeAnalysis.Testing;
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
                        return {|MFFP0015:{{expression}}|};
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

    [Theory]
    [InlineData("fullPath == text", "ordinal")]
    [InlineData("fullPath != text", "ordinal")]
    [InlineData("string.Equals(fullPath, text)", "ordinal")]
    [InlineData("fullPath.Value.Equals(text)", "ordinal")]
    [InlineData("string.Compare(fullPath, text)", "culture-sensitive")]
    [InlineData("fullPath.Value.CompareTo(text)", "culture-sensitive")]
    public async Task Analyzer_MessageIndicatesTheKindOfComparison(string expression, string expectedComparisonKind)
    {
        var source = $$"""
            using System;
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static object M(FullPath fullPath, string text)
                    {
                        return {|#0:{{expression}}|};
                    }
                }
            }
            """;

        var test = CreateAnalyzerTest<CompareFullPathAsStringAnalyzerType>(source);
        test.ExpectedDiagnostics.Add(new DiagnosticResult(CompareFullPathAsStringAnalyzerType.Descriptor)
            .WithLocation(0)
            .WithMessage($"This compares the string representation of a FullPath, which is {expectedComparisonKind}, instead of using FullPathComparer"));

        await test.RunAsync(XunitCancellationToken);
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
