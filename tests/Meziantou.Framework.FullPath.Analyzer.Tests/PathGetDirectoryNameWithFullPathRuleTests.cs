using PathGetDirectoryNameWithFullPathAnalyzerType = Meziantou.Framework.Analyzers.FullPath.PathGetDirectoryNameWithFullPathAnalyzer;
using PathGetDirectoryNameWithFullPathCodeFixProviderType = Meziantou.Framework.Analyzers.FullPath.PathGetDirectoryNameWithFullPathCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class PathGetDirectoryNameWithFullPathRuleTests : FullPathAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForPathGetDirectoryNameOnFullPath()
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
                        return {|MFFP0008:Path.GetDirectoryName(fullPath)|};
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
                        return fullPath.Parent;
                    }
                }
            }
            """;

        await CreateCodeFixTest<PathGetDirectoryNameWithFullPathAnalyzerType, PathGetDirectoryNameWithFullPathCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForPathGetDirectoryNameOnString()
    {
        var source = """
            using System.IO;

            namespace Sample
            {
                public static class TestClass
                {
                    public static string M(string path)
                    {
                        return Path.GetDirectoryName(path);
                    }
                }
            }
            """;

        await CreateAnalyzerTest<PathGetDirectoryNameWithFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotOfferCodeFix_WhenTheResultIsAStringReceiver()
    {
        var source = """
            using System.IO;
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static int M(FullPath fullPath)
                    {
                        return {|MFFP0008:Path.GetDirectoryName(fullPath)|}.Length;
                    }
                }
            }
            """;

        await CreateCodeFixTest<PathGetDirectoryNameWithFullPathAnalyzerType, PathGetDirectoryNameWithFullPathCodeFixProviderType>(source, source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotOfferCodeFix_WhenTheResultInitializesAVar()
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
                        var directory = {|MFFP0008:Path.GetDirectoryName(fullPath)|};
                        return directory.Replace('a', 'b');
                    }
                }
            }
            """;

        await CreateCodeFixTest<PathGetDirectoryNameWithFullPathAnalyzerType, PathGetDirectoryNameWithFullPathCodeFixProviderType>(source, source).RunAsync(XunitCancellationToken);
    }
}
