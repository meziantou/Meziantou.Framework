using CountAssertionAnalyzerType = Meziantou.Framework.Analyzers.Assertions.CountAssertionAnalyzer;
using CountAssertionCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.CountAssertionCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class CountAssertionRuleTests : AssertionsAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForLength()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int[] collection)
                {
                    Assert.Equal(10, {|MFAS0005:collection.Length|});
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int[] collection)
                {
                    Assert.HasCount(10, collection);
                }
            }
            """;

        await CreateCodeFixTest<CountAssertionAnalyzerType, CountAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForCountProperty()
    {
        var source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(ICollection<int> collection)
                {
                    Assert.Equal(10, {|MFAS0005:collection.Count|});
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(ICollection<int> collection)
                {
                    Assert.HasCount(10, collection);
                }
            }
            """;

        await CreateCodeFixTest<CountAssertionAnalyzerType, CountAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForCountMethod()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(IEnumerable<int> collection)
                {
                    Assert.Equal(10, {|MFAS0005:collection.Count()|});
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
                public static void M(IEnumerable<int> collection)
                {
                    Assert.HasCount(10, collection);
                }
            }
            """;

        await CreateCodeFixTest<CountAssertionAnalyzerType, CountAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForTrueWithLengthComparison()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int[] collection)
                {
                    Assert.True({|MFAS0005:collection.Length|} == 10);
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int[] collection)
                {
                    Assert.HasCount(10, collection);
                }
            }
            """;

        await CreateCodeFixTest<CountAssertionAnalyzerType, CountAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForTrueWithCountPropertyComparison()
    {
        var source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(ICollection<int> collection)
                {
                    Assert.True({|MFAS0005:collection.Count|} == 10);
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(ICollection<int> collection)
                {
                    Assert.HasCount(10, collection);
                }
            }
            """;

        await CreateCodeFixTest<CountAssertionAnalyzerType, CountAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForTrueWithCountMethodComparison()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(IEnumerable<int> collection)
                {
                    Assert.True({|MFAS0005:collection.Count()|} == 10);
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
                public static void M(IEnumerable<int> collection)
                {
                    Assert.HasCount(10, collection);
                }
            }
            """;

        await CreateCodeFixTest<CountAssertionAnalyzerType, CountAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ToEmpty_WhenTrueWithLengthComparedToZero()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int[] collection)
                {
                    Assert.True({|MFAS0004:collection.Length|} == 0);
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int[] collection)
                {
                    Assert.Empty(collection);
                }
            }
            """;

        await CreateCodeFixTest<CountAssertionAnalyzerType, CountAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ToEmpty_WhenTrueWithCountPropertyComparedToZero()
    {
        var source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(ICollection<int> collection)
                {
                    Assert.True({|MFAS0004:collection.Count|} == 0);
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(ICollection<int> collection)
                {
                    Assert.Empty(collection);
                }
            }
            """;

        await CreateCodeFixTest<CountAssertionAnalyzerType, CountAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ToEmpty_WhenTrueWithCountMethodComparedToZero()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(IEnumerable<int> collection)
                {
                    Assert.True({|MFAS0004:collection.Count()|} == 0);
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
                public static void M(IEnumerable<int> collection)
                {
                    Assert.Empty(collection);
                }
            }
            """;

        await CreateCodeFixTest<CountAssertionAnalyzerType, CountAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForTrueWithCountMethodNotEqualsComparison()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(IEnumerable<int> collection)
                {
                    Assert.True({|MFAS0005:collection.Count()|} != 10);
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
                public static void M(IEnumerable<int> collection)
                {
                    Assert.DoesNotHaveCount(10, collection);
                }
            }
            """;

        await CreateCodeFixTest<CountAssertionAnalyzerType, CountAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForTrueWithCountPropertyLessThanComparison()
    {
        var source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(ICollection<int> collection)
                {
                    Assert.True({|MFAS0005:collection.Count|} < 10);
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(ICollection<int> collection)
                {
                    Assert.HasCountLessThan(10, collection);
                }
            }
            """;

        await CreateCodeFixTest<CountAssertionAnalyzerType, CountAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForTrueWithCountMethodLessThanOrEqualComparison()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(IEnumerable<int> collection)
                {
                    Assert.True({|MFAS0005:collection.Count()|} <= 10);
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
                public static void M(IEnumerable<int> collection)
                {
                    Assert.HasCountLessThanOrEqual(10, collection);
                }
            }
            """;

        await CreateCodeFixTest<CountAssertionAnalyzerType, CountAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForTrueWithLengthGreaterThanComparison()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int[] collection)
                {
                    Assert.True({|MFAS0005:collection.Length|} > 10);
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int[] collection)
                {
                    Assert.HasCountGreaterThan(10, collection);
                }
            }
            """;

        await CreateCodeFixTest<CountAssertionAnalyzerType, CountAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForTrueWithCountPropertyGreaterThanOrEqualComparison()
    {
        var source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(ICollection<int> collection)
                {
                    Assert.True({|MFAS0005:collection.Count|} >= 10);
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(ICollection<int> collection)
                {
                    Assert.HasCountGreaterThanOrEqual(10, collection);
                }
            }
            """;

        await CreateCodeFixTest<CountAssertionAnalyzerType, CountAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForTrueWithReversedLessThanComparison()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(IEnumerable<int> collection)
                {
                    Assert.True(10 < {|MFAS0005:collection.Count()|});
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
                public static void M(IEnumerable<int> collection)
                {
                    Assert.HasCountGreaterThan(10, collection);
                }
            }
            """;

        await CreateCodeFixTest<CountAssertionAnalyzerType, CountAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForTrueWithReversedGreaterThanOrEqualComparison()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(IEnumerable<int> collection)
                {
                    Assert.True(10 >= {|MFAS0005:collection.Count()|});
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
                public static void M(IEnumerable<int> collection)
                {
                    Assert.HasCountLessThanOrEqual(10, collection);
                }
            }
            """;

        await CreateCodeFixTest<CountAssertionAnalyzerType, CountAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForFalseWithLengthEqualsComparison()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int[] collection)
                {
                    Assert.False({|MFAS0005:collection.Length|} == 10);
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int[] collection)
                {
                    Assert.DoesNotHaveCount(10, collection);
                }
            }
            """;

        await CreateCodeFixTest<CountAssertionAnalyzerType, CountAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForFalseWithLengthComparedToZero()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int[] collection)
                {
                    Assert.False({|MFAS0004:collection.Length|} == 0);
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int[] collection)
                {
                    Assert.NotEmpty(collection);
                }
            }
            """;

        await CreateCodeFixTest<CountAssertionAnalyzerType, CountAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForFalseWithCountMethodNotEqualsComparison()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(IEnumerable<int> collection)
                {
                    Assert.False({|MFAS0005:collection.Count()|} != 10);
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
                public static void M(IEnumerable<int> collection)
                {
                    Assert.HasCount(10, collection);
                }
            }
            """;

        await CreateCodeFixTest<CountAssertionAnalyzerType, CountAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForFalseWithCountPropertyLessThanComparison()
    {
        var source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(ICollection<int> collection)
                {
                    Assert.False({|MFAS0005:collection.Count|} < 10);
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(ICollection<int> collection)
                {
                    Assert.HasCountGreaterThanOrEqual(10, collection);
                }
            }
            """;

        await CreateCodeFixTest<CountAssertionAnalyzerType, CountAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForFalseWithCountMethodLessThanOrEqualComparison()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(IEnumerable<int> collection)
                {
                    Assert.False({|MFAS0005:collection.Count()|} <= 10);
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
                public static void M(IEnumerable<int> collection)
                {
                    Assert.HasCountGreaterThan(10, collection);
                }
            }
            """;

        await CreateCodeFixTest<CountAssertionAnalyzerType, CountAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForFalseWithLengthGreaterThanComparison()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int[] collection)
                {
                    Assert.False({|MFAS0005:collection.Length|} > 10);
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int[] collection)
                {
                    Assert.HasCountLessThanOrEqual(10, collection);
                }
            }
            """;

        await CreateCodeFixTest<CountAssertionAnalyzerType, CountAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForFalseWithCountPropertyGreaterThanOrEqualComparison()
    {
        var source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(ICollection<int> collection)
                {
                    Assert.False({|MFAS0005:collection.Count|} >= 10);
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(ICollection<int> collection)
                {
                    Assert.HasCountLessThan(10, collection);
                }
            }
            """;

        await CreateCodeFixTest<CountAssertionAnalyzerType, CountAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForFalseWithReversedLessThanComparison()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(IEnumerable<int> collection)
                {
                    Assert.False(10 < {|MFAS0005:collection.Count()|});
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
                public static void M(IEnumerable<int> collection)
                {
                    Assert.HasCountLessThanOrEqual(10, collection);
                }
            }
            """;

        await CreateCodeFixTest<CountAssertionAnalyzerType, CountAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForFalseWithReversedGreaterThanOrEqualComparison()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(IEnumerable<int> collection)
                {
                    Assert.False(10 >= {|MFAS0005:collection.Count()|});
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
                public static void M(IEnumerable<int> collection)
                {
                    Assert.HasCountGreaterThan(10, collection);
                }
            }
            """;

        await CreateCodeFixTest<CountAssertionAnalyzerType, CountAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ToEmpty_WhenExpectedCountIsZero()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(IEnumerable<int> collection)
                {
                    Assert.Equal(0, {|MFAS0004:collection.Count()|});
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
                public static void M(IEnumerable<int> collection)
                {
                    Assert.Empty(collection);
                }
            }
            """;

        await CreateCodeFixTest<CountAssertionAnalyzerType, CountAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ToEmpty_WhenExpectedCountIsLongZero()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(IEnumerable<int> collection)
                {
                    Assert.Equal(0L, {|MFAS0004:collection.Count()|});
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
                public static void M(IEnumerable<int> collection)
                {
                    Assert.Empty(collection);
                }
            }
            """;

        await CreateCodeFixTest<CountAssertionAnalyzerType, CountAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForUnrelatedAssertions()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(IEnumerable<int> collection, int value)
                {
                    Assert.Equal(10, value);
                    Assert.Equal(10, collection.Count(i => i > 0));
                    Assert.Equal(10L, collection.Count());
                    Assert.True(collection.Count() == 10L);
                    Assert.True(collection.Count() != 10L);
                    Assert.True(collection.Count() < 10L);
                    Assert.True(collection.Count() <= 10L);
                    Assert.True(collection.Count() > 10L);
                    Assert.True(collection.Count() >= 10L);
                    Assert.False(collection.Count() == 10L);
                    Assert.False(collection.Count() != 10L);
                    Assert.False(collection.Count() < 10L);
                    Assert.False(collection.Count() <= 10L);
                    Assert.False(collection.Count() > 10L);
                    Assert.False(collection.Count() >= 10L);
                    Assert.NotEqual(10L, collection.Count());
                }
            }
            """;

        await CreateAnalyzerTest<CountAssertionAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForCustomCountLikeMembers()
    {
        var source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class EnumerableExtensions
            {
                public static int Count<T>(this IEnumerable<T> source)
                {
                    return 42;
                }
            }

            public sealed class CustomCollection
            {
                public int Count => 10;
                public int Length => 10;
            }

            public static class TestClass
            {
                public static void M(CustomCollection collection, IEnumerable<int> enumerable)
                {
                    Assert.Equal(10, collection.Count);
                    Assert.Equal(10, collection.Length);
                    Assert.Equal(10, enumerable.Count());
                }
            }
            """;

        await CreateAnalyzerTest<CountAssertionAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Theory]
    [InlineData("Assert.NotEqual(0, {|MFAS0004:collection.Count|});", "Assert.NotEmpty(collection);")]
    [InlineData("Assert.NotEqual(3, {|MFAS0005:collection.Count|});", "Assert.DoesNotHaveCount(3, collection);")]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertNotEqualCount(string assertion, string fixedAssertion)
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

        await CreateCodeFixTest<CountAssertionAnalyzerType, CountAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Theory]
    [InlineData("Assert.True({|MFAS0004:collection.Count|} > 0);", "Assert.NotEmpty(collection);")]
    [InlineData("Assert.True({|MFAS0004:collection.Count|} >= 1);", "Assert.NotEmpty(collection);")]
    [InlineData("Assert.True({|MFAS0004:collection.Count|} < 1);", "Assert.Empty(collection);")]
    [InlineData("Assert.True({|MFAS0004:collection.Count|} <= 0);", "Assert.Empty(collection);")]
    [InlineData("Assert.True({|MFAS0004:collection.Count|} != 0);", "Assert.NotEmpty(collection);")]
    [InlineData("Assert.False({|MFAS0004:collection.Count|} > 0);", "Assert.Empty(collection);")]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForEmptinessEquivalentComparison(string assertion, string fixedAssertion)
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

        await CreateCodeFixTest<CountAssertionAnalyzerType, CountAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForStringLength()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.Equal(0, {|MFAS0004:value.Length|});
                    Assert.Equal(3, {|MFAS0005:value.Length|});
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.Empty(value);
                    Assert.HasCount(3, value);
                }
            }
            """;

        await CreateCodeFixTest<CountAssertionAnalyzerType, CountAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForImmutableArrayLength()
    {
        var source = """
            using System.Collections.Immutable;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(ImmutableArray<int> collection)
                {
                    Assert.Equal(0, {|MFAS0004:collection.Length|});
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Immutable;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(ImmutableArray<int> collection)
                {
                    Assert.Empty(collection);
                }
            }
            """;

        await CreateCodeFixTest<CountAssertionAnalyzerType, CountAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }
}
