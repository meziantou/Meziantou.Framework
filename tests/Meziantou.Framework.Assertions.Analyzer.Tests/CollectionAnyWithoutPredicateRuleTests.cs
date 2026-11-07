using CollectionAnyWithoutPredicateAnalyzerType = Meziantou.Framework.Analyzers.Assertions.CollectionAnyWithoutPredicateAnalyzer;
using CollectionAnyWithoutPredicateCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.CollectionAnyWithoutPredicateCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class CollectionAnyWithoutPredicateRuleTests : AssertionsAnalyzerTestBase
{
    [Theory]
    [InlineData("Assert.True({|MFAS0028:collection.Any()|});", "Assert.NotEmpty(collection);")]
    [InlineData("Assert.False({|MFAS0029:collection.Any()|});", "Assert.Empty(collection);")]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForCollectionAny(string assertion, string fixedAssertion)
    {
        var source = $$"""
            using System.Collections.Generic;
            using System.Linq;
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
            using System.Linq;
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

        await CreateCodeFixTest<CollectionAnyWithoutPredicateAnalyzerType, CollectionAnyWithoutPredicateCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_PreservesMessage()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> collection)
                {
                    Assert.True({|MFAS0028:collection.Any()|}, "custom message");
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
                public static void M(List<int> collection)
                {
                    Assert.NotEmpty(collection, message: "custom message");
                }
            }
            """;

        await CreateCodeFixTest<CollectionAnyWithoutPredicateAnalyzerType, CollectionAnyWithoutPredicateCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForAnyWithPredicate()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> collection)
                {
                    Assert.True(collection.Any(x => x > 0));
                    Assert.False(collection.Any(x => x > 0));
                }
            }
            """;

        await CreateAnalyzerTest<CollectionAnyWithoutPredicateAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForCustomAnyMethod()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public sealed class CustomCollection
            {
                public bool Any() => true;
            }

            public static class TestClass
            {
                public static void M(CustomCollection collection)
                {
                    Assert.True(collection.Any());
                }
            }
            """;

        await CreateAnalyzerTest<CollectionAnyWithoutPredicateAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
