using CollectionAllAnalyzerType = Meziantou.Framework.Analyzers.Assertions.CollectionAllAnalyzer;
using CollectionAllCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.CollectionAllCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class CollectionAllRuleTests : AssertionsAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertTrueCollectionAll()
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
                    Assert.True({|MFAS0026:collection.All(x => x > 0)|});
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
                    Assert.All(collection, x => x > 0);
                }
            }
            """;

        await CreateCodeFixTest<CollectionAllAnalyzerType, CollectionAllCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertFalseCollectionAll()
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
                    Assert.False({|MFAS0027:collection.All(x => x > 0)|});
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
                    Assert.DoesNotAll(collection, x => x > 0);
                }
            }
            """;

        await CreateCodeFixTest<CollectionAllAnalyzerType, CollectionAllCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }
}
