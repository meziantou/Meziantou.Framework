using AssertionArgumentOrderAnalyzerType = Meziantou.Framework.Analyzers.Assertions.AssertionArgumentOrderAnalyzer;
using AssertionArgumentOrderCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.AssertionArgumentOrderCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class AssertionArgumentOrderRuleTests : AssertionsAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForEqualWithConstantActual()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.Equal(value, {|MFAS0001:"expected value"|});
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
                    Assert.Equal("expected value", value);
                }
            }
            """;

        await CreateCodeFixTest<AssertionArgumentOrderAnalyzerType, AssertionArgumentOrderCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForNotEqualWithConstantActual()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int value)
                {
                    Assert.NotEqual(value, {|MFAS0001:42|});
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
                    Assert.NotEqual(42, value);
                }
            }
            """;

        await CreateCodeFixTest<AssertionArgumentOrderAnalyzerType, AssertionArgumentOrderCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForEqualWithConstantCollectionExpressionActual()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string[] values)
                {
                    Assert.Equal(values, {|MFAS0001:["expected value", "sample"]|});
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string[] values)
                {
                    Assert.Equal(["expected value", "sample"], values);
                }
            }
            """;

        await CreateCodeFixTest<AssertionArgumentOrderAnalyzerType, AssertionArgumentOrderCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForEqualWithCollectionExpressionActualContainingConstant()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string[] values, string value)
                {
                    Assert.Equal(values, {|MFAS0001:[value, "sample"]|});
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string[] values, string value)
                {
                    Assert.Equal([value, "sample"], values);
                }
            }
            """;

        await CreateCodeFixTest<AssertionArgumentOrderAnalyzerType, AssertionArgumentOrderCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForNamedArguments()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.Equal(expected: value, actual: {|MFAS0001:"expected value"|});
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
                    Assert.Equal(expected: "expected value", actual: value);
                }
            }
            """;

        await CreateCodeFixTest<AssertionArgumentOrderAnalyzerType, AssertionArgumentOrderCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForReorderedNamedArguments()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.Equal(actual: {|MFAS0001:"expected value"|}, expected: value);
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
                    Assert.Equal(actual: value, expected: "expected value");
                }
            }
            """;

        await CreateCodeFixTest<AssertionArgumentOrderAnalyzerType, AssertionArgumentOrderCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForCorrectArgumentOrder_OrBothConstants()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value, string otherValue, string[] values)
                {
                    Assert.Equal("expected value", value);
                    Assert.NotEqual(42, value.Length);
                    Assert.Equal(value, otherValue);
                    Assert.Equal(actual: value, expected: "expected value");
                    Assert.Equal(values, [value, otherValue]);
                    Assert.Equal("expected value", "actual value");
                }
            }
            """;

        await CreateAnalyzerTest<AssertionArgumentOrderAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForUnrelatedAssertType()
    {
        var source = """
            namespace Sample;

            public static class Assert
            {
                public static void Equal(object? expected, object? actual)
                {
                }
            }

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.Equal(value, "expected value");
                }
            }
            """;

        await CreateAnalyzerTest<AssertionArgumentOrderAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
