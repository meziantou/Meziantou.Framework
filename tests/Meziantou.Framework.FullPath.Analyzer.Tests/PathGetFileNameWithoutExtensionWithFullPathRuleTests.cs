using PathGetFileNameWithoutExtensionWithFullPathAnalyzerType = Meziantou.Framework.Analyzers.FullPath.PathGetFileNameWithoutExtensionWithFullPathAnalyzer;
using PathGetFileNameWithoutExtensionWithFullPathCodeFixProviderType = Meziantou.Framework.Analyzers.FullPath.PathGetFileNameWithoutExtensionWithFullPathCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class PathGetFileNameWithoutExtensionWithFullPathRuleTests : FullPathAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForPathGetFileNameWithoutExtensionOnFullPath()
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
                        return {|MFFP0006:Path.GetFileNameWithoutExtension(fullPath)|};
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
                        return fullPath.NameWithoutExtension;
                    }
                }
            }
            """;

        await CreateCodeFixTest<PathGetFileNameWithoutExtensionWithFullPathAnalyzerType, PathGetFileNameWithoutExtensionWithFullPathCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForPathGetFileNameWithoutExtensionOnString()
    {
        var source = """
            using System.IO;

            namespace Sample
            {
                public static class TestClass
                {
                    public static string M(string path)
                    {
                        return Path.GetFileNameWithoutExtension(path);
                    }
                }
            }
            """;

        await CreateAnalyzerTest<PathGetFileNameWithoutExtensionWithFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
