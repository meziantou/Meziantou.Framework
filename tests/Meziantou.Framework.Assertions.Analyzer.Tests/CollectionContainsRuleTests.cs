using CollectionContainsAnalyzerType = Meziantou.Framework.Analyzers.Assertions.CollectionContainsAnalyzer;
using CollectionContainsCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.CollectionContainsCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class CollectionContainsRuleTests : AssertionsAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertTrueCollectionContains()
    {
        var source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> collection, int item)
                {
                    Assert.True({|MFAS0022:collection.Contains(item)|});
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> collection, int item)
                {
                    Assert.Contains(item, collection);
                }
            }
            """;

        await CreateCodeFixTest<CollectionContainsAnalyzerType, CollectionContainsCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertFalseCollectionContains()
    {
        var source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> collection, int item)
                {
                    Assert.False({|MFAS0023:collection.Contains(item)|});
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> collection, int item)
                {
                    Assert.DoesNotContain(item, collection);
                }
            }
            """;

        await CreateCodeFixTest<CollectionContainsAnalyzerType, CollectionContainsCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertTrueDictionaryContainsKey()
    {
        var source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(Dictionary<string, int> dict, string key)
                {
                    Assert.True({|MFAS0022:dict.ContainsKey(key)|}, "should contain key");
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(Dictionary<string, int> dict, string key)
                {
                    Assert.Contains(key, dict, message: "should contain key");
                }
            }
            """;

        await CreateCodeFixTest<CollectionContainsAnalyzerType, CollectionContainsCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertFalseDictionaryContainsKey()
    {
        var source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(Dictionary<string, int> dict, string key)
                {
                    Assert.False({|MFAS0023:dict.ContainsKey(key)|}, "should not contain key");
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(Dictionary<string, int> dict, string key)
                {
                    Assert.DoesNotContain(key, dict, message: "should not contain key");
                }
            }
            """;

        await CreateCodeFixTest<CollectionContainsAnalyzerType, CollectionContainsCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }
}
