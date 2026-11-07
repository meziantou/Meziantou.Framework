using SetConditionAnalyzerType = Meziantou.Framework.Analyzers.Assertions.SetConditionAnalyzer;
using SetConditionCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.SetConditionCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class SetConditionRuleTests : AssertionsAnalyzerTestBase
{
    [Theory]
    [InlineData("Assert.True({|MFAS0041:set.IsProperSubsetOf(other)|});", "Assert.ProperSubset(other, set);")]
    [InlineData("Assert.False({|MFAS0042:set.IsProperSubsetOf(other)|});", "Assert.NotProperSubset(other, set);")]
    [InlineData("Assert.True({|MFAS0043:set.IsProperSupersetOf(other)|});", "Assert.ProperSuperset(other, set);")]
    [InlineData("Assert.False({|MFAS0044:set.IsProperSupersetOf(other)|});", "Assert.NotProperSuperset(other, set);")]
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
    public async Task CodeFix_UsesTheReceiverAsActual_WhenTheArgumentIsNotASet()
    {
        var source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(ISet<string> set, IEnumerable<string> other)
                {
                    Assert.True({|MFAS0041:set.IsProperSubsetOf(other)|});
                    Assert.False({|MFAS0044:set.IsProperSupersetOf(new[] { "a" })|});
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(ISet<string> set, IEnumerable<string> other)
                {
                    Assert.ProperSubset(other, set);
                    Assert.NotProperSuperset(new[] { "a" }, set);
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
