using AssertionMethodSelectionAnalyzerType = Meziantou.Framework.Analyzers.Assertions.AssertionMethodSelectionAnalyzer;
using AssertionMethodSelectionCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.AssertionMethodSelectionCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class AssertionMethodSelectionRuleTests : AssertionsAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertTrueEqualsNull()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string? value)
                {
                    Assert.True({|MFAS0006:value|} == null);
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string? value)
                {
                    Assert.Null(value);
                }
            }
            """;

        await CreateCodeFixTest<AssertionMethodSelectionAnalyzerType, AssertionMethodSelectionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertTrueNotEqualsNull()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string? value)
                {
                    Assert.True({|MFAS0007:value|} != null);
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string? value)
                {
                    Assert.NotNull(value);
                }
            }
            """;

        await CreateCodeFixTest<AssertionMethodSelectionAnalyzerType, AssertionMethodSelectionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertTrueIsNull()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string? value)
                {
                    Assert.True({|MFAS0006:value|} is null);
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string? value)
                {
                    Assert.Null(value);
                }
            }
            """;

        await CreateCodeFixTest<AssertionMethodSelectionAnalyzerType, AssertionMethodSelectionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertTrueIsNotNull()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string? value)
                {
                    Assert.True({|MFAS0007:value|} is not null);
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string? value)
                {
                    Assert.NotNull(value);
                }
            }
            """;

        await CreateCodeFixTest<AssertionMethodSelectionAnalyzerType, AssertionMethodSelectionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertNullWithValueType()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int value)
                {
                    Assert.Null({|MFAS0008:value|});
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int value)
                {
                    Assert.Equal(default(int), value);
                }
            }
            """;

        await CreateCodeFixTest<AssertionMethodSelectionAnalyzerType, AssertionMethodSelectionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertNotNullWithValueType()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int value)
                {
                    Assert.NotNull({|MFAS0009:value|});
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int value)
                {
                    Assert.NotEqual(default(int), value);
                }
            }
            """;

        await CreateCodeFixTest<AssertionMethodSelectionAnalyzerType, AssertionMethodSelectionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertSameWithValueType()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int expected, int actual)
                {
                    {|MFAS0010:Assert.Same(expected, actual)|};
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int expected, int actual)
                {
                    Assert.Equal(expected, actual);
                }
            }
            """;

        await CreateCodeFixTest<AssertionMethodSelectionAnalyzerType, AssertionMethodSelectionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertNotSameWithValueType()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int expected, int actual)
                {
                    {|MFAS0011:Assert.NotSame(expected, actual)|};
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int expected, int actual)
                {
                    Assert.NotEqual(expected, actual);
                }
            }
            """;

        await CreateCodeFixTest<AssertionMethodSelectionAnalyzerType, AssertionMethodSelectionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForValidNullAndSameAssertions()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string? value, object expected, object actual)
                {
                    int? nullableValue = value?.Length;
                    Assert.Null(nullableValue);
                    Assert.NotNull(nullableValue);
                    Assert.Same(expected, actual);
                    Assert.NotSame(expected, actual);
                }
            }
            """;

        await CreateAnalyzerTest<AssertionMethodSelectionAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
