using CollectionAnyAnalyzerType = Meziantou.Framework.Analyzers.Assertions.CollectionAnyAnalyzer;
using CollectionAnyCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.CollectionAnyCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class CollectionAnyRuleTests : AssertionsAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertTrueCollectionAny()
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
                    Assert.True({|MFAS0024:collection.Any(x => x > 0)|});
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

        await CreateCodeFixTest<CollectionAnyAnalyzerType, CollectionAnyCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertFalseCollectionAny()
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
                    Assert.False({|MFAS0025:collection.Any(x => x > 0)|});
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

        await CreateCodeFixTest<CollectionAnyAnalyzerType, CollectionAnyCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }
}
