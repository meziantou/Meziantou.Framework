using RedundantPathPredicateAnalyzerType = Meziantou.Framework.Analyzers.FullPath.RedundantPathPredicateAnalyzer;

namespace Meziantou.Framework.Tests;

public sealed class RedundantPathPredicateRuleTests : FullPathAnalyzerTestBase
{
    [Theory]
    [InlineData("IsPathRooted")]
    [InlineData("IsPathFullyQualified")]
    public async Task Analyzer_ReportDiagnostic_ForPredicateOnFullPath(string methodName)
    {
        var source = $$"""
            using System.IO;
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static bool M(FullPath fullPath)
                    {
                        return {|MFFP0018:Path.{{methodName}}(fullPath)|};
                    }
                }
            }
            """;

        await CreateAnalyzerTest<RedundantPathPredicateAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForPredicateOnString()
    {
        var source = """
            using System.IO;

            namespace Sample
            {
                public static class TestClass
                {
                    public static bool M(string path)
                    {
                        return Path.IsPathRooted(path);
                    }
                }
            }
            """;

        await CreateAnalyzerTest<RedundantPathPredicateAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
