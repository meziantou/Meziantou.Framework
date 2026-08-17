using SequenceEqualAnalyzerType = Meziantou.Framework.Analyzers.Assertions.SequenceEqualAnalyzer;
using SequenceEqualCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.SequenceEqualCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class SequenceEqualRuleTests : AssertionsAnalyzerTestBase
{
    [Theory]
    [InlineData("Assert.True({|MFAS0035:actual.SequenceEqual(expected)|});", "Assert.Equal(expected, actual);")]
    [InlineData("Assert.False({|MFAS0036:actual.SequenceEqual(expected)|});", "Assert.NotEqual(expected, actual);")]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForSequenceEqual(string assertion, string fixedAssertion)
    {
        var source = $$"""
            using System.Collections.Generic;
            using System.Linq;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> actual, List<int> expected)
                {
                    {{assertion}}
                }
            }
            """;

        var fixedSource = $$"""
            using System.Collections.Generic;
            using System.Linq;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> actual, List<int> expected)
                {
                    {{fixedAssertion}}
                }
            }
            """;

        await CreateCodeFixTest<SequenceEqualAnalyzerType, SequenceEqualCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForCustomSequenceEqualAndComparerOverload()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public sealed class CustomCollection
            {
                public bool SequenceEqual(CustomCollection other) => true;
            }

            public static class TestClass
            {
                public static void M(CustomCollection a, CustomCollection b, List<int> actual, List<int> expected)
                {
                    Assert.True(a.SequenceEqual(b));

                    // The comparer overload has no matching rewrite
                    Assert.True(actual.SequenceEqual(expected, EqualityComparer<int>.Default));
                }
            }
            """;

        await CreateAnalyzerTest<SequenceEqualAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForStaticInvocationForm()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> actual, List<int> expected)
                {
                    Assert.True({|MFAS0035:Enumerable.SequenceEqual(actual, expected)|});
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Generic;
            using System.Linq;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> actual, List<int> expected)
                {
                    Assert.Equal(expected, actual);
                }
            }
            """;

        await CreateCodeFixTest<SequenceEqualAnalyzerType, SequenceEqualCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }
}
