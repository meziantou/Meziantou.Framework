using FullPathDivisionWithFullPathRightAnalyzerType = Meziantou.Framework.Analyzers.FullPath.FullPathDivisionWithFullPathRightAnalyzer;
using FullPathDivisionWithFullPathRightCodeFixProviderType = Meziantou.Framework.Analyzers.FullPath.FullPathDivisionWithFullPathRightCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class FullPathDivisionWithFullPathRightRuleTests : FullPathAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForFullPathDivisionWithFullPathRight()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static FullPath M(FullPath left, FullPath right)
                    {
                        return {|MFFP0002:left / right|};
                    }
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static FullPath M(FullPath left, FullPath right)
                    {
                        return right;
                    }
                }
            }
            """;

        await CreateCodeFixTest<FullPathDivisionWithFullPathRightAnalyzerType, FullPathDivisionWithFullPathRightCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForFullPathDivisionWithStringRight()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static FullPath M(FullPath left)
                    {
                        return left / "child";
                    }
                }
            }
            """;

        await CreateAnalyzerTest<FullPathDivisionWithFullPathRightAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForOtherTypeDivisionOperatorWithFullPathRight()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public readonly struct OtherPath
                {
                    public static OtherPath operator /(OtherPath left, string right) => left;
                }

                public static class TestClass
                {
                    public static OtherPath M(OtherPath left, FullPath right)
                    {
                        return left / right;
                    }
                }
            }
            """;

        await CreateAnalyzerTest<FullPathDivisionWithFullPathRightAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
