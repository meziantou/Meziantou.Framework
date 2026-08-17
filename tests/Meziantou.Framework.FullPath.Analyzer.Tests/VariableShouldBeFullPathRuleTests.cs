using VariableShouldBeFullPathAnalyzerType = Meziantou.Framework.Analyzers.FullPath.VariableShouldBeFullPathAnalyzer;

namespace Meziantou.Framework.Tests;

public sealed class VariableShouldBeFullPathRuleTests : FullPathAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_ForStringLocalInitializedWithFullPath()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static string M(FullPath fullPath)
                    {
                        string {|MFFP0013:path|} = fullPath;
                        return path;
                    }
                }
            }
            """;

        await CreateAnalyzerTest<VariableShouldBeFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_WhenEveryAssignmentIsFullPath()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static string M(bool condition, FullPath value1, FullPath value2)
                    {
                        string {|MFFP0013:path|} = value1;
                        if (condition)
                            path = value2;

                        return path;
                    }
                }
            }
            """;

        await CreateAnalyzerTest<VariableShouldBeFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_WhenAnyAssignmentIsNotFullPath()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static string M(bool condition, FullPath value)
                    {
                        string path = value;
                        if (condition)
                            path = "text";

                        return path;
                    }
                }
            }
            """;

        await CreateAnalyzerTest<VariableShouldBeFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForVarDeclaration()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static string M(FullPath fullPath)
                    {
                        var path = fullPath.Value;
                        return path;
                    }
                }
            }
            """;

        await CreateAnalyzerTest<VariableShouldBeFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_WhenVariableIsPassedByReference()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static string M(FullPath fullPath)
                    {
                        string path = fullPath;
                        Update(ref path);
                        return path;
                    }

                    private static void Update(ref string value) => value = "text";
                }
            }
            """;

        await CreateAnalyzerTest<VariableShouldBeFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_WhenVariableIsAppendedTo()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static string M(FullPath fullPath)
                    {
                        string path = fullPath;
                        path += ".bak";
                        return path;
                    }
                }
            }
            """;

        await CreateAnalyzerTest<VariableShouldBeFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForStringLocalWithoutValue()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static string M(FullPath fullPath)
                    {
                        string path;
                        path = "text";
                        return path;
                    }
                }
            }
            """;

        await CreateAnalyzerTest<VariableShouldBeFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
