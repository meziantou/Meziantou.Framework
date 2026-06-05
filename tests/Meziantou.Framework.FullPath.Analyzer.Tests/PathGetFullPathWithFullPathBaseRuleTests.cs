using PathGetFullPathWithFullPathBaseAnalyzerType = Meziantou.Framework.Analyzers.FullPath.PathGetFullPathWithFullPathBaseAnalyzer;
using PathGetFullPathWithFullPathBaseCodeFixProviderType = Meziantou.Framework.Analyzers.FullPath.PathGetFullPathWithFullPathBaseCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class PathGetFullPathWithFullPathBaseRuleTests : FullPathAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForPathGetFullPathWithFullPathBase()
    {
        var source = """
            using System.IO;
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static void M(string value, FullPath basePath)
                    {
                        _ = {|MFFP0003:Path.GetFullPath(value, basePath)|};
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
                    public static void M(string value, FullPath basePath)
                    {
                        _ = basePath / value;
                    }
                }
            }
            """;

        await CreateCodeFixTest<PathGetFullPathWithFullPathBaseAnalyzerType, PathGetFullPathWithFullPathBaseCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForPathGetFullPathWithStringBase()
    {
        var source = """
            using System.IO;

            namespace Sample
            {
                public static class TestClass
                {
                    public static void M(string value, string basePath)
                    {
                        _ = Path.GetFullPath(value, basePath);
                    }
                }
            }
            """;

        await CreateAnalyzerTest<PathGetFullPathWithFullPathBaseAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
