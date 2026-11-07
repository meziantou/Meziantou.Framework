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
    public async Task Analyzer_DoesNotReportDiagnostic_WhenVariableIsCoalesceAssigned()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static string M(FullPath fullPath, string other)
                    {
                        string path = fullPath;
                        path ??= other;
                        return path;
                    }
                }
            }
            """;

        await CreateAnalyzerTest<VariableShouldBeFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_WhenVariableIsDeconstructionTarget()
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
                        int count;
                        (path, count) = ("relative", 1);
                        return path + count;
                    }
                }
            }
            """;

        await CreateAnalyzerTest<VariableShouldBeFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_WhenVariableIsNestedDeconstructionTarget()
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
                        ((var count, path), var flag) = ((1, "relative"), true);
                        return path + count + flag;
                    }
                }
            }
            """;

        await CreateAnalyzerTest<VariableShouldBeFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_WhenVariableIsAliasedByRefLocal()
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
                        ref string alias = ref path;
                        alias = "relative";
                        return path;
                    }
                }
            }
            """;

        await CreateAnalyzerTest<VariableShouldBeFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_WhenVariableIsAliasedByRefAssignment()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static string M(FullPath fullPath, bool condition)
                    {
                        string path = fullPath;
                        string other = "";
                        ref string alias = ref other;
                        alias = ref condition ? ref other : ref path;
                        alias = "relative";
                        return path;
                    }
                }
            }
            """;

        await CreateAnalyzerTest<VariableShouldBeFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_WhenVariableIsAliasedByRefReturn()
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
                        Pick(ref path) = "relative";
                        return path;
                    }

                    private static ref string Pick(ref string value) => ref value;
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
