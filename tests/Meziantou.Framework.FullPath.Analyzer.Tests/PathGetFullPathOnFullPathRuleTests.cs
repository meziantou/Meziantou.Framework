using PathGetFullPathOnFullPathAnalyzerType = Meziantou.Framework.Analyzers.FullPath.PathGetFullPathOnFullPathAnalyzer;
using PathGetFullPathOnFullPathCodeFixProviderType = Meziantou.Framework.Analyzers.FullPath.PathGetFullPathOnFullPathCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class PathGetFullPathOnFullPathRuleTests : FullPathAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForPathGetFullPathOnFullPath()
    {
        var source = """
            using System.IO;
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static void M(FullPath value)
                    {
                        _ = {|MFFP0001:Path.GetFullPath(value)|};
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
                    public static void M(FullPath value)
                    {
                        _ = value;
                    }
                }
            }
            """;

        await CreateCodeFixTest<PathGetFullPathOnFullPathAnalyzerType, PathGetFullPathOnFullPathCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForPathGetFullPathOnString()
    {
        var source = """
            using System.IO;

            namespace Sample
            {
                public static class TestClass
                {
                    public static void M(string value)
                    {
                        _ = Path.GetFullPath(value);
                    }
                }
            }
            """;

        await CreateAnalyzerTest<PathGetFullPathOnFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
