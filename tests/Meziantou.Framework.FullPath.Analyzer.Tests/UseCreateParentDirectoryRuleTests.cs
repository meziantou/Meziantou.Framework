using DirectoryGetParentWithFullPathAnalyzerType = Meziantou.Framework.Analyzers.FullPath.DirectoryGetParentWithFullPathAnalyzer;
using UseCreateParentDirectoryAnalyzerType = Meziantou.Framework.Analyzers.FullPath.UseCreateParentDirectoryAnalyzer;

namespace Meziantou.Framework.Tests;

public sealed class UseCreateParentDirectoryRuleTests : FullPathAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_ForCreateDirectoryWithParent()
    {
        var source = """
            using System.IO;
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static void M(FullPath fullPath)
                    {
                        {|MFFP0022:Directory.CreateDirectory(fullPath.Parent)|};
                    }
                }
            }
            """;

        await CreateAnalyzerTest<UseCreateParentDirectoryAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForCreateDirectoryWithFullPath()
    {
        var source = """
            using System.IO;
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static void M(FullPath fullPath)
                    {
                        Directory.CreateDirectory(fullPath);
                    }
                }
            }
            """;

        await CreateAnalyzerTest<UseCreateParentDirectoryAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_ForDirectoryGetParentWithFullPath()
    {
        var source = """
            using System.IO;
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static DirectoryInfo M(FullPath fullPath)
                    {
                        return {|MFFP0023:Directory.GetParent(fullPath)|};
                    }
                }
            }
            """;

        await CreateAnalyzerTest<DirectoryGetParentWithFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForDirectoryGetParentWithString()
    {
        var source = """
            using System.IO;

            namespace Sample
            {
                public static class TestClass
                {
                    public static DirectoryInfo M(string path)
                    {
                        return Directory.GetParent(path);
                    }
                }
            }
            """;

        await CreateAnalyzerTest<DirectoryGetParentWithFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
