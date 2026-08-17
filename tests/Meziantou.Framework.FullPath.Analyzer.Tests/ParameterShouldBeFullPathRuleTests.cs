using ParameterShouldBeFullPathAnalyzerType = Meziantou.Framework.Analyzers.FullPath.ParameterShouldBeFullPathAnalyzer;

namespace Meziantou.Framework.Tests;

public sealed class ParameterShouldBeFullPathRuleTests : FullPathAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_WhenEveryCallSitePassesAFullPath()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static void M(FullPath value1, FullPath value2)
                    {
                        Process(value1);
                        Process(value2);
                    }

                    private static void Process(string {|MFFP0014:path|})
                    {
                    }
                }
            }
            """;

        await CreateAnalyzerTest<ParameterShouldBeFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_ForLocalFunction()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static void M(FullPath fullPath)
                    {
                        Process(fullPath);

                        static void Process(string {|MFFP0014:path|})
                        {
                        }
                    }
                }
            }
            """;

        await CreateAnalyzerTest<ParameterShouldBeFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_WhenAnyCallSitePassesAString()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static void M(FullPath fullPath)
                    {
                        Process(fullPath);
                        Process("text");
                    }

                    private static void Process(string path)
                    {
                    }
                }
            }
            """;

        await CreateAnalyzerTest<ParameterShouldBeFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_ForInternalMethod()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static void M(FullPath fullPath)
                    {
                        Process(fullPath);
                    }

                    internal static void Process(string {|MFFP0014:path|})
                    {
                    }
                }
            }
            """;

        await CreateAnalyzerTest<ParameterShouldBeFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_ForPublicMethodOfATypeThatIsNotVisibleOutsideOfTheAssembly()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                internal static class Helper
                {
                    public static void Process(string {|MFFP0014:path|})
                    {
                    }
                }

                public static class TestClass
                {
                    public static void M(FullPath fullPath)
                    {
                        Helper.Process(fullPath);
                    }
                }
            }
            """;

        await CreateAnalyzerTest<ParameterShouldBeFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForMethodVisibleOutsideOfTheAssembly()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static void M(FullPath fullPath)
                    {
                        Process(fullPath);
                    }

                    public static void Process(string path)
                    {
                    }
                }
            }
            """;

        await CreateAnalyzerTest<ParameterShouldBeFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForProtectedMethod()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public class TestClass
                {
                    public void M(FullPath fullPath)
                    {
                        Process(fullPath);
                    }

                    protected void Process(string path)
                    {
                    }
                }
            }
            """;

        await CreateAnalyzerTest<ParameterShouldBeFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_WhenMethodIsNeverCalled()
    {
        var source = """
            namespace Sample
            {
                public static class TestClass
                {
                    private static void Process(string path)
                    {
                    }
                }
            }
            """;

        await CreateAnalyzerTest<ParameterShouldBeFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_WhenMethodIsUsedAsADelegate()
    {
        var source = """
            using System;
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static Action<string> M(FullPath fullPath)
                    {
                        Process(fullPath);
                        return Process;
                    }

                    private static void Process(string path)
                    {
                    }
                }
            }
            """;

        await CreateAnalyzerTest<ParameterShouldBeFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForNullableParameter()
    {
        var source = """
            #nullable enable
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static void M(FullPath fullPath)
                    {
                        Process(fullPath);
                    }

                    private static void Process(string? path)
                    {
                    }
                }
            }
            """;

        await CreateAnalyzerTest<ParameterShouldBeFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForOptionalParameter()
    {
        var source = """
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static void M(FullPath fullPath)
                    {
                        Process(fullPath);
                    }

                    private static void Process(string path = "")
                    {
                    }
                }
            }
            """;

        await CreateAnalyzerTest<ParameterShouldBeFullPathAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
