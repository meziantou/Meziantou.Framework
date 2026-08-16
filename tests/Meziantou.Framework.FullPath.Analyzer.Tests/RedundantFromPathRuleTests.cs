using RedundantFromPathAnalyzerType = Meziantou.Framework.Analyzers.FullPath.RedundantFromPathAnalyzer;

namespace Meziantou.Framework.Tests;

public sealed class RedundantFromPathRuleTests : FullPathAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_ForFromPathWithFullPath()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static FullPath M(FullPath fullPath)
                    {
                        return {|MFFP0019:FullPath.FromPath(fullPath)|};
                    }
                }
            }
            """;

        await CreateAnalyzerTest<RedundantFromPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_ForFromPathWithPathGetFullPath()
    {
        var source = """
            using System.IO;
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static FullPath M(string path)
                    {
                        return {|MFFP0019:FullPath.FromPath(Path.GetFullPath(path))|};
                    }
                }
            }
            """;

        await CreateAnalyzerTest<RedundantFromPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_ForFromPathWithPathCombine()
    {
        var source = """
            using System.IO;
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static FullPath M(string path1, string path2)
                    {
                        return {|MFFP0019:FullPath.FromPath(Path.Combine(path1, path2))|};
                    }
                }
            }
            """;

        await CreateAnalyzerTest<RedundantFromPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForFromPathWithString()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static FullPath M(string path)
                    {
                        return FullPath.FromPath(path);
                    }
                }
            }
            """;

        await CreateAnalyzerTest<RedundantFromPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
