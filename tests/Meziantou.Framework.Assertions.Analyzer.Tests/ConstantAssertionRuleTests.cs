using ConstantAssertionAnalyzerType = Meziantou.Framework.Analyzers.Assertions.ConstantAssertionAnalyzer;

namespace Meziantou.Framework.Tests;

public sealed class ConstantAssertionRuleTests : AssertionsAnalyzerTestBase
{
    [Theory]
    [InlineData("Assert.True(true)")]
    [InlineData("Assert.False(false)")]
    [InlineData("Assert.Equal(value, value)")]
    [InlineData("Assert.NotEqual(value, value)")]
    [InlineData("Assert.Same(reference, reference)")]
    [InlineData("Assert.NotSame(reference, reference)")]
    [InlineData("Assert.Equivalent(reference, reference)")]
    public async Task Analyzer_ReportDiagnostic_ForAssertionWithConstantResult(string assertion)
    {
        var source = $$"""
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int value, object reference)
                {
                    {|MFAS0050:{{assertion}}|};
                }
            }
            """;

        await CreateAnalyzerTest<ConstantAssertionAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForMeaningfulAssertions()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                private static int Counter;

                public static void M(int value, int other, object reference, object otherReference, bool condition)
                {
                    Assert.True(condition);
                    Assert.False(condition);
                    Assert.Equal(value, other);
                    Assert.Same(reference, otherReference);

                    // Assert.True(false) is reported by MFAS0051 instead
                    Assert.True(false);
                    Assert.False(true);

                    // Calls may have side effects, so comparing two of them is not necessarily pointless
                    Assert.Equal(Next(), Next());
                }

                private static int Next() => Counter++;
            }
            """;

        await CreateAnalyzerTest<ConstantAssertionAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForReflexivityAssertionOnCustomType()
    {
        // Assert.Equal goes through EqualityComparer<T>.Default and therefore runs Version.Equals, so this is a
        // reflexivity assertion that fails when that implementation is wrong. Assert.Same never runs user code.
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public sealed class Version
            {
                public override bool Equals(object? obj) => obj is Version;
                public override int GetHashCode() => 0;
            }

            public static class TestClass
            {
                public static void M(Version left, int number)
                {
                    Assert.Equal(left, left);
                    Assert.NotEqual(left, left);

                    {|MFAS0050:Assert.Equivalent(left, left)|};
                    {|MFAS0050:Assert.Same(left, left)|};
                    {|MFAS0050:Assert.Equal(number, number)|};
                }
            }
            """;

        await CreateAnalyzerTest<ConstantAssertionAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_WhenTheInstanceMayHaveSideEffects()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public sealed class Box
            {
                public int Value;
            }

            public static class TestClass
            {
                private static int Counter;

                public static void M()
                {
                    // Each call returns a different box, so this is not a comparison of a value with itself
                    Assert.Equal(Next().Value, Next().Value);
                }

                private static Box Next() => new Box { Value = Counter++ };
            }
            """;

        await CreateAnalyzerTest<ConstantAssertionAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_ForFieldReachedThroughSideEffectFreeInstance()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public sealed class Box
            {
                public int Value;

                public void M(Box other)
                {
                    {|MFAS0050:Assert.Equal(this.Value, this.Value)|};
                    {|MFAS0050:Assert.Equal(other.Value, other.Value)|};
                    Assert.Equal(this.Value, other.Value);
                }
            }
            """;

        await CreateAnalyzerTest<ConstantAssertionAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
