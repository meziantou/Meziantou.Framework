using MethodShouldReturnFullPathAnalyzerType = Meziantou.Framework.Analyzers.FullPath.MethodShouldReturnFullPathAnalyzer;

namespace Meziantou.Framework.Tests;

public sealed class MethodShouldReturnFullPathRuleTests : FullPathAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_WhenAllReturnsAreFullPathInStringMethod()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static string {|MFFP0011:M|}(bool condition, FullPath value1, FullPath value2)
                    {
                        if (condition)
                            return value1;

                        return value2;
                    }
                }
            }
            """;

        await CreateAnalyzerTest<MethodShouldReturnFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_ForExpressionBodiedMethodReturningFullPathEmpty()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static string {|MFFP0011:Sample|}() => FullPath.Empty;
                }
            }
            """;

        await CreateAnalyzerTest<MethodShouldReturnFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_ForLocalFunctionReturningFullPathEmpty()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static string M()
                    {
                        string {|MFFP0011:Sample|}() => FullPath.Empty;
                        return Sample();
                    }
                }
            }
            """;

        await CreateAnalyzerTest<MethodShouldReturnFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_WhenAnyReturnIsNotFullPath()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static string M(bool condition, FullPath value)
                    {
                        if (condition)
                            return value;

                        return "text";
                    }
                }
            }
            """;

        await CreateAnalyzerTest<MethodShouldReturnFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_WhenMethodDoesNotReturnString()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static FullPath M(FullPath value)
                    {
                        return value;
                    }
                }
            }
            """;

        await CreateAnalyzerTest<MethodShouldReturnFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
