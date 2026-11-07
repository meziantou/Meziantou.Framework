using RedundantFromPathAnalyzerType = Meziantou.Framework.Analyzers.FullPath.RedundantFromPathAnalyzer;
using RedundantFromPathCodeFixProviderType = Meziantou.Framework.Analyzers.FullPath.RedundantFromPathCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class RedundantFromPathRuleTests : FullPathAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForFromPathWithFullPath()
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

        var fixedSource = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static FullPath M(FullPath fullPath)
                    {
                        return fullPath;
                    }
                }
            }
            """;

        await CreateCodeFixTest<RedundantFromPathAnalyzerType, RedundantFromPathCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForFromPathWithPathGetFullPath()
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

        var fixedSource = """
            using System.IO;
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

        await CreateCodeFixTest<RedundantFromPathAnalyzerType, RedundantFromPathCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForFromPathWithPathCombine()
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

        var fixedSource = """
            using System.IO;
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static FullPath M(string path1, string path2)
                    {
                        return FullPath.Combine(path1, path2);
                    }
                }
            }
            """;

        await CreateCodeFixTest<RedundantFromPathAnalyzerType, RedundantFromPathCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
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

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForFromPathWithPathJoin()
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
                        return FullPath.FromPath(Path.Join(path1, path2));
                    }
                }
            }
            """;

        await CreateAnalyzerTest<RedundantFromPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForFromPathWithPathJoinOverSpans()
    {
        var source = """
            using System;
            using System.IO;
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static FullPath M(string path1, string path2)
                    {
                        return FullPath.FromPath(Path.Join(path1.AsSpan(), path2.AsSpan()));
                    }
                }
            }
            """;

        await CreateAnalyzerTest<RedundantFromPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
