using RedundantPathPredicateAnalyzerType = Meziantou.Framework.Analyzers.FullPath.RedundantPathPredicateAnalyzer;
using RedundantPathPredicateCodeFixProviderType = Meziantou.Framework.Analyzers.FullPath.RedundantPathPredicateCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class RedundantPathPredicateRuleTests : FullPathAnalyzerTestBase
{
    [Theory]
    [InlineData("IsPathRooted")]
    [InlineData("IsPathFullyQualified")]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForPredicateOnFullPath(string methodName)
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

        var fixedSource = """
            using System.IO;
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static bool M(FullPath fullPath)
                    {
                        return !fullPath.IsEmpty;
                    }
                }
            }
            """;

        await CreateCodeFixTest<RedundantPathPredicateAnalyzerType, RedundantPathPredicateCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
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

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_WhenTheResultIsAReceiver()
    {
        var source = """
            using System.IO;
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static string M(FullPath fullPath)
                    {
                        return {|MFFP0018:Path.IsPathRooted(fullPath)|}.ToString();
                    }
                }
            }
            """;

        var fixedSource = """
            using System.IO;
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static string M(FullPath fullPath)
                    {
                        return (!fullPath.IsEmpty).ToString();
                    }
                }
            }
            """;

        await CreateCodeFixTest<RedundantPathPredicateAnalyzerType, RedundantPathPredicateCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }
}
