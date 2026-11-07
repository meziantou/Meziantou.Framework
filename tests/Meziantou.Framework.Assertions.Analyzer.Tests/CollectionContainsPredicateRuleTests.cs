using CollectionContainsPredicateAnalyzerType = Meziantou.Framework.Analyzers.Assertions.CollectionContainsPredicateAnalyzer;
using CollectionContainsPredicateCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.CollectionContainsPredicateCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class CollectionContainsPredicateRuleTests : AssertionsAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForContainsWithEqualityPredicate()
    {
        var source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> collection)
                {
                    {|MFAS0054:Assert.Contains(collection, x => x == 1)|};
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
                    Assert.Contains(1, collection);
                }
            }
            """;

        await CreateCodeFixTest<CollectionContainsPredicateAnalyzerType, CollectionContainsPredicateCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForDoesNotContainWithEqualityPredicate()
    {
        var source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<string> collection, string expected)
                {
                    {|MFAS0055:Assert.DoesNotContain(collection, x => expected == x)|};
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<string> collection, string expected)
                {
                    Assert.DoesNotContain(expected, collection);
                }
            }
            """;

        await CreateCodeFixTest<CollectionContainsPredicateAnalyzerType, CollectionContainsPredicateCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_KeepsMessageAsNamedArgument()
    {
        var source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> collection)
                {
                    {|MFAS0054:Assert.Contains(collection, x => x == 1, "message")|};
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
                    Assert.Contains(1, collection, message: "message");
                }
            }
            """;

        await CreateCodeFixTest<CollectionContainsPredicateAnalyzerType, CollectionContainsPredicateCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForEnumValue()
    {
        var source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public enum Sample1 { A, B }

            public static class TestClass
            {
                public static void M(List<Sample1> collection)
                {
                    {|MFAS0054:Assert.Contains(collection, x => x == Sample1.B)|};
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public enum Sample1 { A, B }

            public static class TestClass
            {
                public static void M(List<Sample1> collection)
                {
                    Assert.Contains(Sample1.B, collection);
                }
            }
            """;

        await CreateCodeFixTest<CollectionContainsPredicateAnalyzerType, CollectionContainsPredicateCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForPredicateUsingTheItem()
    {
        var source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> collection)
                {
                    Assert.Contains(collection, x => x > 0);
                    Assert.Contains(collection, x => x == x + 1);
                }
            }
            """;

        await CreateAnalyzerTest<CollectionContainsPredicateAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForFloatingPointValue()
    {
        var source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<double> collection)
                {
                    Assert.Contains(collection, x => x == 1d);
                }
            }
            """;

        await CreateAnalyzerTest<CollectionContainsPredicateAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForCustomType()
    {
        var source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public sealed class Item
            {
                public int Value { get; set; }
            }

            public static class TestClass
            {
                public static void M(List<Item> collection, Item expected)
                {
                    Assert.Contains(collection, x => x == expected);
                }
            }
            """;

        await CreateAnalyzerTest<CollectionContainsPredicateAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForExpectedValueAssertion()
    {
        var source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> collection)
                {
                    Assert.Contains(1, collection);
                    Assert.DoesNotContain(1, collection);
                }
            }
            """;

        await CreateAnalyzerTest<CollectionContainsPredicateAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
