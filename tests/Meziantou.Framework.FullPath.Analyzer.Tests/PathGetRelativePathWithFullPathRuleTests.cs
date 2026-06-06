using PathGetRelativePathWithFullPathAnalyzerType = Meziantou.Framework.Analyzers.FullPath.PathGetRelativePathWithFullPathAnalyzer;
using PathGetRelativePathWithFullPathCodeFixProviderType = Meziantou.Framework.Analyzers.FullPath.PathGetRelativePathWithFullPathCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class PathGetRelativePathWithFullPathRuleTests : FullPathAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForPathGetRelativePathOnFullPaths()
    {
        var source = """
            using System.IO;
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static string M(FullPath root, FullPath value)
                    {
                        return {|MFFP0010:Path.GetRelativePath(root, value)|};
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
                    public static string M(FullPath root, FullPath value)
                    {
                        return value.MakePathRelativeTo(root);
                    }
                }
            }
            """;

        await CreateCodeFixTest<PathGetRelativePathWithFullPathAnalyzerType, PathGetRelativePathWithFullPathCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForPathGetRelativePathOnStrings()
    {
        var source = """
            using System.IO;

            namespace Sample
            {
                public static class TestClass
                {
                    public static string M(string root, string value)
                    {
                        return Path.GetRelativePath(root, value);
                    }
                }
            }
            """;

        await CreateAnalyzerTest<PathGetRelativePathWithFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
