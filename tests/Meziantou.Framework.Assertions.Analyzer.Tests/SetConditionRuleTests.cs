using SetConditionAnalyzerType = Meziantou.Framework.Analyzers.Assertions.SetConditionAnalyzer;
using SetConditionCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.SetConditionCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class SetConditionRuleTests : AssertionsAnalyzerTestBase
{
    [Theory]
    [InlineData("Assert.True({|MFAS0041:set.IsProperSubsetOf(other)|});", "Assert.ProperSubset(set, other);")]
    [InlineData("Assert.False({|MFAS0042:set.IsProperSubsetOf(other)|});", "Assert.NotProperSubset(set, other);")]
    [InlineData("Assert.True({|MFAS0043:set.IsProperSupersetOf(other)|});", "Assert.ProperSuperset(set, other);")]
    [InlineData("Assert.False({|MFAS0044:set.IsProperSupersetOf(other)|});", "Assert.NotProperSuperset(set, other);")]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForSetOperations(string assertion, string fixedAssertion)
    {
        var source = $$"""
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(HashSet<int> set, HashSet<int> other)
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
                public static void M(HashSet<int> set, HashSet<int> other)
                {
                    {{fixedAssertion}}
                }
            }
            """;

        await CreateCodeFixTest<SetConditionAnalyzerType, SetConditionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForCustomSetLikeType()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public sealed class CustomSet
            {
                public bool IsProperSubsetOf(CustomSet other) => true;
                public bool IsProperSupersetOf(CustomSet other) => true;
            }

            public static class TestClass
            {
                public static void M(CustomSet set, CustomSet other)
                {
                    Assert.True(set.IsProperSubsetOf(other));
                    Assert.True(set.IsProperSupersetOf(other));
                }
            }
            """;

        await CreateAnalyzerTest<SetConditionAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
