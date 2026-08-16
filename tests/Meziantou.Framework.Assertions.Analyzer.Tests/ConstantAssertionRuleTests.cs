using ConstantAssertionAnalyzerType = Meziantou.Framework.Analyzers.Assertions.ConstantAssertionAnalyzer;

namespace Meziantou.Framework.Tests;

public sealed class ConstantAssertionRuleTests : AssertionsAnalyzerTestBase
{
    [Theory]
    [InlineData("Assert.True(true)")]
    [InlineData("Assert.False(false)")]
    [InlineData("Assert.Equal(value, value)")]
    [InlineData("Assert.NotEqual(value, value)")]
    [InlineData("Assert.Same(reference, reference)")]
    [InlineData("Assert.NotSame(reference, reference)")]
    [InlineData("Assert.Equivalent(reference, reference)")]
    public async Task Analyzer_ReportDiagnostic_ForAssertionWithConstantResult(string assertion)
    {
        var source = $$"""
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int value, object reference)
                {
                    {|MFAS0050:{{assertion}}|};
                }
            }
            """;

        await CreateAnalyzerTest<ConstantAssertionAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForMeaningfulAssertions()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                private static int Counter;

                public static void M(int value, int other, object reference, object otherReference, bool condition)
                {
                    Assert.True(condition);
                    Assert.False(condition);
                    Assert.Equal(value, other);
                    Assert.Same(reference, otherReference);

                    // Assert.True(false) is reported by MFAS0051 instead
                    Assert.True(false);
                    Assert.False(true);

                    // Calls may have side effects, so comparing two of them is not necessarily pointless
                    Assert.Equal(Next(), Next());
                }

                private static int Next() => Counter++;
            }
            """;

        await CreateAnalyzerTest<ConstantAssertionAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
