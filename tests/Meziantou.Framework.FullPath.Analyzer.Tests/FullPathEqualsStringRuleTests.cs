using FullPathEqualsStringAnalyzerType = Meziantou.Framework.Analyzers.FullPath.FullPathEqualsStringAnalyzer;

namespace Meziantou.Framework.Tests;

public sealed class FullPathEqualsStringRuleTests : FullPathAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_ForEqualsWithStringLiteral()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static bool M(FullPath fullPath)
                    {
                        return {|MFFP0016:fullPath.Equals("value")|};
                    }
                }
            }
            """;

        await CreateAnalyzerTest<FullPathEqualsStringAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_ForEqualsWithStringVariable()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static bool M(FullPath fullPath, string text)
                    {
                        return {|MFFP0016:fullPath.Equals(text)|};
                    }
                }
            }
            """;

        await CreateAnalyzerTest<FullPathEqualsStringAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForEqualsWithFullPath()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static bool M(FullPath fullPath, FullPath other)
                    {
                        return fullPath.Equals(other);
                    }
                }
            }
            """;

        await CreateAnalyzerTest<FullPathEqualsStringAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForEqualsWithObject()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static bool M(FullPath fullPath, object value)
                    {
                        return fullPath.Equals(value);
                    }
                }
            }
            """;

        await CreateAnalyzerTest<FullPathEqualsStringAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForStringEqualsOnFullPathValue()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static bool M(FullPath fullPath, string text)
                    {
                        return fullPath.Value.Equals(text);
                    }
                }
            }
            """;

        await CreateAnalyzerTest<FullPathEqualsStringAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_ForEqualsWithFullPathValue()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static bool M(FullPath fullPath, FullPath other)
                    {
                        return {|MFFP0016:fullPath.Equals(other.Value)|};
                    }
                }
            }
            """;

        await CreateAnalyzerTest<FullPathEqualsStringAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_ForEqualsWithFullPathRawValue()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static bool M(FullPath fullPath, FullPath other)
                    {
                        return {|MFFP0016:fullPath.Equals(other.RawValue)|};
                    }
                }
            }
            """;

        await CreateAnalyzerTest<FullPathEqualsStringAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_ForEqualsWithFullPathToString()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static bool M(FullPath fullPath, FullPath other)
                    {
                        return {|MFFP0016:fullPath.Equals(other.ToString())|};
                    }
                }
            }
            """;

        await CreateAnalyzerTest<FullPathEqualsStringAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_ForEqualsWithFullPathExplicitCast()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static bool M(FullPath fullPath, FullPath other)
                    {
                        return {|MFFP0016:fullPath.Equals((string)other)|};
                    }
                }
            }
            """;

        await CreateAnalyzerTest<FullPathEqualsStringAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
