using RuntimeTypeConditionAnalyzerType = Meziantou.Framework.Analyzers.Assertions.RuntimeTypeConditionAnalyzer;
using RuntimeTypeConditionCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.RuntimeTypeConditionCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class RuntimeTypeConditionRuleTests : AssertionsAnalyzerTestBase
{
    [Theory]
    [InlineData("Assert.True({|MFAS0046:value.GetType() == typeof(string)|});", "Assert.IsType<string>(value);")]
    [InlineData("Assert.True({|MFAS0046:typeof(string) == value.GetType()|});", "Assert.IsType<string>(value);")]
    [InlineData("Assert.True({|MFAS0047:value.GetType() != typeof(string)|});", "Assert.IsNotType<string>(value);")]
    [InlineData("Assert.False({|MFAS0047:value.GetType() == typeof(string)|});", "Assert.IsNotType<string>(value);")]
    [InlineData("Assert.False({|MFAS0046:value.GetType() != typeof(string)|});", "Assert.IsType<string>(value);")]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForRuntimeTypeComparison(string assertion, string fixedAssertion)
    {
        var source = $$"""
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(object value)
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
                public static void M(object value)
                {
                    {{fixedAssertion}}
                }
            }
            """;

        await CreateCodeFixTest<RuntimeTypeConditionAnalyzerType, RuntimeTypeConditionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForOtherTypeComparisons()
    {
        var source = """
            using System;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(object value, Type type)
                {
                    Assert.True(value.GetType() == type);
                    Assert.True(type == typeof(string));
                }
            }
            """;

        await CreateAnalyzerTest<RuntimeTypeConditionAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
