using ReferenceEqualsConditionAnalyzerType = Meziantou.Framework.Analyzers.Assertions.ReferenceEqualsConditionAnalyzer;
using ReferenceEqualsConditionCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.ReferenceEqualsConditionCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class ReferenceEqualsConditionRuleTests : AssertionsAnalyzerTestBase
{
    [Theory]
    [InlineData("Assert.True({|MFAS0037:object.ReferenceEquals(a, b)|});", "Assert.Same(a, b);")]
    [InlineData("Assert.False({|MFAS0038:object.ReferenceEquals(a, b)|});", "Assert.NotSame(a, b);")]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForReferenceEquals(string assertion, string fixedAssertion)
    {
        var source = $$"""
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(object a, object b)
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
                public static void M(object a, object b)
                {
                    {{fixedAssertion}}
                }
            }
            """;

        await CreateCodeFixTest<ReferenceEqualsConditionAnalyzerType, ReferenceEqualsConditionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForValueTypes()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int a, int b)
                {
                    // Assert.Same reports MFAS0010 for value types
                    Assert.True(object.ReferenceEquals(a, b));
                }
            }
            """;

        await CreateAnalyzerTest<ReferenceEqualsConditionAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
