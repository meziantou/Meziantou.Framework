using CollectionWhereAssertionAnalyzerType = Meziantou.Framework.Analyzers.Assertions.CollectionWhereAssertionAnalyzer;
using CollectionWhereAssertionCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.CollectionWhereAssertionCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class CollectionWhereAssertionRuleTests : AssertionsAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForNotEmptyWithWhere()
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
                    {|MFAS0051:Assert.NotEmpty(collection.Where(x => x > 0))|};
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
                    Assert.Contains(collection, x => x > 0);
                }
            }
            """;

        await CreateCodeFixTest<CollectionWhereAssertionAnalyzerType, CollectionWhereAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForEmptyWithWhere()
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
                    {|MFAS0052:Assert.Empty(collection.Where(x => x > 0))|};
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
                    Assert.DoesNotContain(collection, x => x > 0);
                }
            }
            """;

        await CreateCodeFixTest<CollectionWhereAssertionAnalyzerType, CollectionWhereAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForSingleWithWhere()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static int M(List<int> collection)
                {
                    return {|MFAS0053:Assert.Single(collection.Where(x => x > 0))|};
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
                public static int M(List<int> collection)
                {
                    return Assert.Single(collection, x => x > 0);
                }
            }
            """;

        await CreateCodeFixTest<CollectionWhereAssertionAnalyzerType, CollectionWhereAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_KeepsMessage()
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
                    {|MFAS0051:Assert.NotEmpty(collection.Where(x => x > 0), "message")|};
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
                    Assert.Contains(collection, x => x > 0, "message");
                }
            }
            """;

        await CreateCodeFixTest<CollectionWhereAssertionAnalyzerType, CollectionWhereAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForNamedArgument()
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
                    {|MFAS0052:Assert.Empty(actual: collection.Where(x => x > 0))|};
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
                    Assert.DoesNotContain(actual: collection, predicate: x => x > 0);
                }
            }
            """;

        await CreateCodeFixTest<CollectionWhereAssertionAnalyzerType, CollectionWhereAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForIndexedWhere()
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
                    Assert.NotEmpty(collection.Where((x, i) => x > i));
                }
            }
            """;

        await CreateAnalyzerTest<CollectionWhereAssertionAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForCollection()
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
                    Assert.NotEmpty(collection);
                    Assert.Empty(collection);
                    Assert.Single(collection);
                }
            }
            """;

        await CreateAnalyzerTest<CollectionWhereAssertionAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForSingleWithPredicate()
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
                    Assert.Single(collection, x => x > 0);
                }
            }
            """;

        await CreateAnalyzerTest<CollectionWhereAssertionAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
