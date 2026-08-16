using NegatedConditionAnalyzerType = Meziantou.Framework.Analyzers.Assertions.NegatedConditionAnalyzer;
using NegatedConditionCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.NegatedConditionCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class NegatedConditionRuleTests : AssertionsAnalyzerTestBase
{
    [Theory]
    [InlineData("Assert.True({|MFAS0045:!condition|});", "Assert.False(condition);")]
    [InlineData("Assert.False({|MFAS0045:!condition|});", "Assert.True(condition);")]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForNegatedCondition(string assertion, string fixedAssertion)
    {
        var source = $$"""
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(bool condition)
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
                public static void M(bool condition)
                {
                    {{fixedAssertion}}
                }
            }
            """;

        await CreateCodeFixTest<NegatedConditionAnalyzerType, NegatedConditionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForNonNegatedCondition()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(bool condition, int value)
                {
                    Assert.True(condition);
                    Assert.False(condition);
                    Assert.True(~value == 0);
                }
            }
            """;

        await CreateAnalyzerTest<NegatedConditionAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
