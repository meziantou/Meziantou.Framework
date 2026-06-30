using StringContainsAnalyzerType = Meziantou.Framework.Analyzers.Assertions.StringContainsAnalyzer;
using StringContainsCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.StringContainsCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class StringContainsRuleTests : AssertionsAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertTrueStringContains()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.True({|MFAS0016:value.Contains("abc")|});
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.Contains("abc", value);
                }
            }
            """;

        await CreateCodeFixTest<StringContainsAnalyzerType, StringContainsCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertFalseStringContains()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.False({|MFAS0017:value.Contains("abc")|});
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.DoesNotContain("abc", value);
                }
            }
            """;

        await CreateCodeFixTest<StringContainsAnalyzerType, StringContainsCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertTrueStringContainsWithOrdinalIgnoreCase()
    {
        var source = """
            using System;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.True({|MFAS0016:value.Contains("abc", StringComparison.OrdinalIgnoreCase)|});
                }
            }
            """;

        var fixedSource = """
            using System;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.Contains("abc", value, true);
                }
            }
            """;

        await CreateCodeFixTest<StringContainsAnalyzerType, StringContainsCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertTrueStringContainsWithOrdinal()
    {
        var source = """
            using System;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.True({|MFAS0016:value.Contains("abc", StringComparison.Ordinal)|});
                }
            }
            """;

        var fixedSource = """
            using System;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.Contains("abc", value);
                }
            }
            """;

        await CreateCodeFixTest<StringContainsAnalyzerType, StringContainsCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForAssertTrueStringContainsWithCurrentCulture()
    {
        var source = """
            using System;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.True(value.Contains("abc", StringComparison.CurrentCulture));
                }
            }
            """;

        await CreateAnalyzerTest<StringContainsAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
