using PathGetExtensionWithFullPathAnalyzerType = Meziantou.Framework.Analyzers.FullPath.PathGetExtensionWithFullPathAnalyzer;
using PathGetExtensionWithFullPathCodeFixProviderType = Meziantou.Framework.Analyzers.FullPath.PathGetExtensionWithFullPathCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class PathGetExtensionWithFullPathRuleTests : FullPathAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForPathGetExtensionOnFullPath()
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
                        return {|MFFP0007:Path.GetExtension(fullPath)|};
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
                        return fullPath.Extension;
                    }
                }
            }
            """;

        await CreateCodeFixTest<PathGetExtensionWithFullPathAnalyzerType, PathGetExtensionWithFullPathCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForPathGetExtensionOnString()
    {
        var source = """
            using System.IO;

            namespace Sample
            {
                public static class TestClass
                {
                    public static string M(string path)
                    {
                        return Path.GetExtension(path);
                    }
                }
            }
            """;

        await CreateAnalyzerTest<PathGetExtensionWithFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
