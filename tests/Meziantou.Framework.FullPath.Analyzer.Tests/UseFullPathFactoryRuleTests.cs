using UseFullPathFactoryAnalyzerType = Meziantou.Framework.Analyzers.FullPath.UseFullPathFactoryAnalyzer;
using UseFullPathFactoryCodeFixProviderType = Meziantou.Framework.Analyzers.FullPath.UseFullPathFactoryCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class UseFullPathFactoryRuleTests : FullPathAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForPathGetTempPath()
    {
        var source = """
            using System.IO;
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static FullPath M()
                    {
                        return FullPath.Combine({|MFFP0024:Path.GetTempPath()|}, "sample");
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
                    public static FullPath M()
                    {
                        return FullPath.Combine(FullPath.GetTempPath(), "sample");
                    }
                }
            }
            """;

        await CreateCodeFixTest<UseFullPathFactoryAnalyzerType, UseFullPathFactoryCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForPathGetTempPathAssignedToString()
    {
        var source = """
            using System.IO;

            namespace Sample
            {
                public static class TestClass
                {
                    public static string M()
                    {
                        return {|MFFP0024:Path.GetTempPath()|};
                    }
                }
            }
            """;

        var fixedSource = """
            using System.IO;

            namespace Sample
            {
                public static class TestClass
                {
                    public static string M()
                    {
                        return Meziantou.Framework.FullPath.GetTempPath();
                    }
                }
            }
            """;

        await CreateCodeFixTest<UseFullPathFactoryAnalyzerType, UseFullPathFactoryCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForEnvironmentGetFolderPath()
    {
        var source = """
            using System;
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static FullPath M()
                    {
                        return FullPath.Combine({|MFFP0024:Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)|}, "sample");
                    }
                }
            }
            """;

        var fixedSource = """
            using System;
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static FullPath M()
                    {
                        return FullPath.Combine(FullPath.GetFolderPath(Environment.SpecialFolder.MyDocuments), "sample");
                    }
                }
            }
            """;

        await CreateCodeFixTest<UseFullPathFactoryAnalyzerType, UseFullPathFactoryCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForEnvironmentGetFolderPathWithSpecialFolderOption()
    {
        var source = """
            using System;

            namespace Sample
            {
                public static class TestClass
                {
                    public static string M()
                    {
                        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments, Environment.SpecialFolderOption.Create);
                    }
                }
            }
            """;

        await CreateAnalyzerTest<UseFullPathFactoryAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForFullPathFactoryMethods()
    {
        var source = """
            using System;
            using Meziantou.Framework;

            namespace Sample
            {
                public static class TestClass
                {
                    public static FullPath M()
                    {
                        return FullPath.Combine(FullPath.GetTempPath(), FullPath.GetFolderPath(Environment.SpecialFolder.MyDocuments).Name);
                    }
                }
            }
            """;

        await CreateAnalyzerTest<UseFullPathFactoryAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForUnrelatedMethods()
    {
        var source = """
            using System.IO;

            namespace Sample
            {
                public static class TestClass
                {
                    public static string M()
                    {
                        return Path.GetTempFileName();
                    }
                }
            }
            """;

        await CreateAnalyzerTest<UseFullPathFactoryAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
