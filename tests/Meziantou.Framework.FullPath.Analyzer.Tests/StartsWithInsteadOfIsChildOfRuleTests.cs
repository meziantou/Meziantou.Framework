using StartsWithInsteadOfIsChildOfAnalyzerType = Meziantou.Framework.Analyzers.FullPath.StartsWithInsteadOfIsChildOfAnalyzer;

namespace Meziantou.Framework.Tests;

public sealed class StartsWithInsteadOfIsChildOfRuleTests : FullPathAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_ForStartsWithOnFullPathValues()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static bool M(FullPath child, FullPath root)
                    {
                        return {|MFFP0017:child.Value.StartsWith(root.Value)|};
                    }
                }
            }
            """;

        await CreateAnalyzerTest<StartsWithInsteadOfIsChildOfAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_ForStartsWithWithStringComparison()
    {
        var source = """
            using System;
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static bool M(FullPath child, FullPath root)
                    {
                        return {|MFFP0017:child.Value.StartsWith(root.Value, StringComparison.Ordinal)|};
                    }
                }
            }
            """;

        await CreateAnalyzerTest<StartsWithInsteadOfIsChildOfAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForStartsWithOnAStringPrefix()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static bool M(FullPath child)
                    {
                        return child.Value.StartsWith("prefix");
                    }
                }
            }
            """;

        await CreateAnalyzerTest<StartsWithInsteadOfIsChildOfAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForIsChildOf()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static bool M(FullPath child, FullPath root)
                    {
                        return child.IsChildOf(root);
                    }
                }
            }
            """;

        await CreateAnalyzerTest<StartsWithInsteadOfIsChildOfAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
