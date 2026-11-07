using EmptinessAssertionAnalyzerType = Meziantou.Framework.Analyzers.Assertions.EmptinessAssertionAnalyzer;
using EmptinessAssertionCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.EmptinessAssertionCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class EmptinessAssertionRuleTests : AssertionsAnalyzerTestBase
{
    [Theory]
    [InlineData("{|MFAS0030:Assert.HasCount(0, collection)|};", "Assert.Empty(collection);")]
    [InlineData("{|MFAS0030:Assert.HasCountLessThan(1, collection)|};", "Assert.Empty(collection);")]
    [InlineData("{|MFAS0030:Assert.HasCountLessThanOrEqual(0, collection)|};", "Assert.Empty(collection);")]
    [InlineData("{|MFAS0030:Assert.DoesNotHaveCount(0, collection)|};", "Assert.NotEmpty(collection);")]
    [InlineData("{|MFAS0030:Assert.HasCountGreaterThan(0, collection)|};", "Assert.NotEmpty(collection);")]
    [InlineData("{|MFAS0030:Assert.HasCountGreaterThanOrEqual(1, collection)|};", "Assert.NotEmpty(collection);")]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForCountAssertionAgainstZero(string assertion, string fixedAssertion)
    {
        var source = $$"""
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> collection)
                {
                    {{assertion}}
                }
            }
            """;

        var fixedSource = $$"""
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> collection)
                {
                    {{fixedAssertion}}
                }
            }
            """;

        await CreateCodeFixTest<EmptinessAssertionAnalyzerType, EmptinessAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_PreservesMessage()
    {
        var source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> collection)
                {
                    {|MFAS0030:Assert.HasCount(0, collection, "custom message")|};
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> collection)
                {
                    Assert.Empty(collection, "custom message");
                }
            }
            """;

        await CreateCodeFixTest<EmptinessAssertionAnalyzerType, EmptinessAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForNonZeroCounts()
    {
        var source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> collection, int count)
                {
                    Assert.HasCount(1, collection);
                    Assert.HasCount(count, collection);
                    Assert.HasCountGreaterThan(1, collection);
                    Assert.HasCountGreaterThanOrEqual(0, collection);
                    Assert.HasCountLessThan(2, collection);
                    Assert.HasCountLessThanOrEqual(1, collection);
                    Assert.DoesNotHaveCount(1, collection);
                }
            }
            """;

        await CreateAnalyzerTest<EmptinessAssertionAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Theory]
    [InlineData("{|MFAS0030:Assert.HasCount(expectedCount: 0, actual: collection)|};", "Assert.Empty(actual: collection);")]
    [InlineData("{|MFAS0030:Assert.HasCount(actual: collection, expectedCount: 0)|};", "Assert.Empty(actual: collection);")]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForNamedArguments(string assertion, string fixedAssertion)
    {
        var source = $$"""
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> collection)
                {
                    {{assertion}}
                }
            }
            """;

        var fixedSource = $$"""
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> collection)
                {
                    {{fixedAssertion}}
                }
            }
            """;

        await CreateCodeFixTest<EmptinessAssertionAnalyzerType, EmptinessAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }
}
