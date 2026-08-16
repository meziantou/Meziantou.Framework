using UseFailAssertionAnalyzerType = Meziantou.Framework.Analyzers.Assertions.UseFailAssertionAnalyzer;
using UseFailAssertionCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.UseFailAssertionCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class UseFailAssertionRuleTests : AssertionsAnalyzerTestBase
{
    [Theory]
    [InlineData("{|MFAS0051:Assert.True(false)|};", "Assert.Fail();")]
    [InlineData("{|MFAS0051:Assert.False(true)|};", "Assert.Fail();")]
    [InlineData("""{|MFAS0051:Assert.True(false, "unreachable")|};""", """Assert.Fail("unreachable");""")]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForUnconditionalFailure(string assertion, string fixedAssertion)
    {
        var source = $$"""
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M()
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
                public static void M()
                {
                    {{fixedAssertion}}
                }
            }
            """;

        await CreateCodeFixTest<UseFailAssertionAnalyzerType, UseFailAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForAssertionsThatCanSucceed()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(bool condition)
                {
                    Assert.True(condition);
                    Assert.False(condition);

                    // Always succeeds, reported by MFAS0050 instead
                    Assert.True(true);
                    Assert.False(false);
                }
            }
            """;

        await CreateAnalyzerTest<UseFailAssertionAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
