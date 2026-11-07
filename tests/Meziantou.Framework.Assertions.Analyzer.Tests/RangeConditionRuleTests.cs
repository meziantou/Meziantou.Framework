using RangeConditionAnalyzerType = Meziantou.Framework.Analyzers.Assertions.RangeConditionAnalyzer;
using RangeConditionCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.RangeConditionCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class RangeConditionRuleTests : AssertionsAnalyzerTestBase
{
    [Theory]
    [InlineData("Assert.True({|MFAS0039:value >= 0 && value <= 10|});", "Assert.InRange(value, 0, 10);")]
    [InlineData("Assert.True({|MFAS0039:0 <= value && value <= 10|});", "Assert.InRange(value, 0, 10);")]
    [InlineData("Assert.True({|MFAS0039:value <= 10 && value >= 0|});", "Assert.InRange(value, 0, 10);")]
    [InlineData("Assert.True({|MFAS0039:value >= 0 && 10 >= value|});", "Assert.InRange(value, 0, 10);")]
    [InlineData("Assert.False({|MFAS0040:value >= 0 && value <= 10|});", "Assert.NotInRange(value, 0, 10);")]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForRangeCheck(string assertion, string fixedAssertion)
    {
        var source = $$"""
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int value)
                {
                    {{assertion}}
                }
            }
            """;

        var fixedSource = $$"""
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int value)
                {
                    {{fixedAssertion}}
                }
            }
            """;

        await CreateCodeFixTest<RangeConditionAnalyzerType, RangeConditionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForComparableType()
    {
        var source = """
            using System;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(DateTime value, DateTime low, DateTime high)
                {
                    Assert.True({|MFAS0039:value >= low && value <= high|});
                }
            }
            """;

        var fixedSource = """
            using System;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(DateTime value, DateTime low, DateTime high)
                {
                    Assert.InRange(value, low, high);
                }
            }
            """;

        await CreateCodeFixTest<RangeConditionAnalyzerType, RangeConditionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Theory]
    [InlineData("Assert.True({|MFAS0039:low <= value && value <= high|});", "Assert.InRange(value, low, high);")]
    [InlineData("Assert.True({|MFAS0039:value >= low && value <= high|});", "Assert.InRange(value, low, high);")]
    [InlineData("Assert.True({|MFAS0039:value <= high && low <= value|});", "Assert.InRange(value, low, high);")]
    [InlineData("Assert.True({|MFAS0039:high >= value && low <= value|});", "Assert.InRange(value, low, high);")]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForVariableBounds(string assertion, string fixedAssertion)
    {
        var source = $$"""
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int value, int low, int high)
                {
                    {{assertion}}
                }
            }
            """;

        var fixedSource = $$"""
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int value, int low, int high)
                {
                    {{fixedAssertion}}
                }
            }
            """;

        await CreateCodeFixTest<RangeConditionAnalyzerType, RangeConditionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForMemberAccessValue()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public sealed class Box
            {
                public int Value { get; set; }
            }

            public static class TestClass
            {
                public static void M(Box box, int low, int high)
                {
                    Assert.True({|MFAS0039:low <= box.Value && box.Value <= high|});
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public sealed class Box
            {
                public int Value { get; set; }
            }

            public static class TestClass
            {
                public static void M(Box box, int low, int high)
                {
                    Assert.InRange(box.Value, low, high);
                }
            }
            """;

        await CreateCodeFixTest<RangeConditionAnalyzerType, RangeConditionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForExclusiveBoundsOrDifferentValues()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int value, int other)
                {
                    // Assert.InRange is inclusive on both ends
                    Assert.True(value > 0 && value <= 10);
                    Assert.True(value >= 0 && value < 10);

                    // Two different values are not a range check
                    Assert.True(value >= 0 && other <= 10);

                    // Two lower bounds are not a range check
                    Assert.True(value >= 0 && value >= 10);
                }
            }
            """;

        await CreateAnalyzerTest<RangeConditionAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
