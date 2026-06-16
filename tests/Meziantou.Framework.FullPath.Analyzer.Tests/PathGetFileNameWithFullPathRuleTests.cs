using PathGetFileNameWithFullPathAnalyzerType = Meziantou.Framework.Analyzers.FullPath.PathGetFileNameWithFullPathAnalyzer;
using PathGetFileNameWithFullPathCodeFixProviderType = Meziantou.Framework.Analyzers.FullPath.PathGetFileNameWithFullPathCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class PathGetFileNameWithFullPathRuleTests : FullPathAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForPathGetFileNameOnFullPath()
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
                        return {|MFFP0005:Path.GetFileName(fullPath)|};
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
                        return fullPath.Name;
                    }
                }
            }
            """;

        await CreateCodeFixTest<PathGetFileNameWithFullPathAnalyzerType, PathGetFileNameWithFullPathCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForPathGetFileNameOnString()
    {
        var source = """
            using System.IO;

            namespace Sample
            {
                public static class TestClass
                {
                    public static string M(string path)
                    {
                        return Path.GetFileName(path);
                    }
                }
            }
            """;

        await CreateAnalyzerTest<PathGetFileNameWithFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
