using PathCombineWithFullPathAnalyzerType = Meziantou.Framework.Analyzers.FullPath.PathCombineWithFullPathAnalyzer;
using PathCombineWithFullPathCodeFixProviderType = Meziantou.Framework.Analyzers.FullPath.PathCombineWithFullPathCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class PathCombineWithFullPathRuleTests : FullPathAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForPathCombineWithFullPathFirstAndOneValue()
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
                        return {|MFFP0004:Path.Combine(fullPath, "value")|};
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
                        return fullPath / "value";
                    }
                }
            }
            """;

        await CreateCodeFixTest<PathCombineWithFullPathAnalyzerType, PathCombineWithFullPathCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForPathCombineWithFullPathFirstAndMultipleValues()
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
                        return {|MFFP0004:Path.Combine(fullPath, "value1", "value2")|};
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
                        return fullPath / "value1" / "value2";
                    }
                }
            }
            """;

        await CreateCodeFixTest<PathCombineWithFullPathAnalyzerType, PathCombineWithFullPathCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForPathCombineWithFullPathLast()
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
                        return {|MFFP0004:Path.Combine("value1", "value2", fullPath)|};
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
                        return fullPath;
                    }
                }
            }
            """;

        await CreateCodeFixTest<PathCombineWithFullPathAnalyzerType, PathCombineWithFullPathCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForPathCombineWithStrings()
    {
        var source = """
            using System.IO;

            namespace Sample
            {
                public static class TestClass
                {
                    public static string M()
                    {
                        return Path.Combine("value1", "value2");
                    }
                }
            }
            """;

        await CreateAnalyzerTest<PathCombineWithFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
