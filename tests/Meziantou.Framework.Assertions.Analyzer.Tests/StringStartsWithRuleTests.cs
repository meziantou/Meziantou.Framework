using StringStartsWithAnalyzerType = Meziantou.Framework.Analyzers.Assertions.StringStartsWithAnalyzer;
using StringStartsWithCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.StringStartsWithCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class StringStartsWithRuleTests : AssertionsAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertTrueStringStartsWith()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.True({|MFAS0018:value.StartsWith("abc")|});
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
                    Assert.StartsWith("abc", value);
                }
            }
            """;

        await CreateCodeFixTest<StringStartsWithAnalyzerType, StringStartsWithCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertFalseStringStartsWith()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.False({|MFAS0019:value.StartsWith("abc")|});
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
                    Assert.DoesNotStartWith("abc", value);
                }
            }
            """;

        await CreateCodeFixTest<StringStartsWithAnalyzerType, StringStartsWithCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertTrueStringStartsWithIgnoreCase()
    {
        var source = """
            using System;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.True({|MFAS0018:value.StartsWith("abc", StringComparison.OrdinalIgnoreCase)|});
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
                    Assert.StartsWith("abc", value, true);
                }
            }
            """;

        await CreateCodeFixTest<StringStartsWithAnalyzerType, StringStartsWithCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertTrueStringStartsWithIgnoreCaseBool()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.True({|MFAS0018:value.StartsWith("abc", ignoreCase: true, culture: null)|});
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
                    Assert.StartsWith("abc", value, true);
                }
            }
            """;

        await CreateCodeFixTest<StringStartsWithAnalyzerType, StringStartsWithCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }
}
