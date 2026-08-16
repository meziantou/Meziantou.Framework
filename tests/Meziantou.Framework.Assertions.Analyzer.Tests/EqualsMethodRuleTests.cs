using EqualsMethodAnalyzerType = Meziantou.Framework.Analyzers.Assertions.EqualsMethodAnalyzer;
using EqualsMethodCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.EqualsMethodCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class EqualsMethodRuleTests : AssertionsAnalyzerTestBase
{
    [Theory]
    [InlineData("Assert.True({|MFAS0033:value.Equals(other)|});", "Assert.Equal(other, value);")]
    [InlineData("Assert.False({|MFAS0034:value.Equals(other)|});", "Assert.NotEqual(other, value);")]
    [InlineData("Assert.True({|MFAS0033:object.Equals(value, other)|});", "Assert.Equal(value, other);")]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForEqualsMethod(string assertion, string fixedAssertion)
    {
        var source = $$"""
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int value, int other)
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
                public static void M(int value, int other)
                {
                    {{fixedAssertion}}
                }
            }
            """;

        await CreateCodeFixTest<EqualsMethodAnalyzerType, EqualsMethodCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Theory]
    [InlineData("Assert.True({|MFAS0033:string.Equals(a, b, StringComparison.OrdinalIgnoreCase)|});", "Assert.Equal(a, b, ignoreCase: true);")]
    [InlineData("Assert.True({|MFAS0033:string.Equals(a, b, StringComparison.Ordinal)|});", "Assert.Equal(a, b);")]
    [InlineData("Assert.True({|MFAS0033:a.Equals(b, StringComparison.OrdinalIgnoreCase)|});", "Assert.Equal(b, a, ignoreCase: true);")]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForStringEquals(string assertion, string fixedAssertion)
    {
        var source = $$"""
            using System;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string a, string b)
                {
                    {{assertion}}
                }
            }
            """;

        var fixedSource = $$"""
            using System;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string a, string b)
                {
                    {{fixedAssertion}}
                }
            }
            """;

        await CreateCodeFixTest<EqualsMethodAnalyzerType, EqualsMethodCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForSequenceTypesAndUnsupportedComparisons()
    {
        var source = """
            using System;
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> collection, List<int> other, string a, string b)
                {
                    // Assert.Equal compares sequences element by element, Equals does not
                    Assert.True(collection.Equals(other));
                    Assert.True(object.Equals(collection, other));

                    // Only Ordinal and OrdinalIgnoreCase map onto the ignoreCase parameter
                    Assert.True(string.Equals(a, b, StringComparison.InvariantCulture));
                    Assert.True(a.Equals(b, StringComparison.CurrentCulture));
                }
            }
            """;

        await CreateAnalyzerTest<EqualsMethodAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
