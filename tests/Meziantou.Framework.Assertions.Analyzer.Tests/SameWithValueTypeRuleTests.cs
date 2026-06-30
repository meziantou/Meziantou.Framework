using SameWithValueTypeAnalyzerType = Meziantou.Framework.Analyzers.Assertions.SameWithValueTypeAnalyzer;
using SameWithValueTypeCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.SameWithValueTypeCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class SameWithValueTypeRuleTests : AssertionsAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertSameWithValueType()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int expected, int actual)
                {
                    {|MFAS0010:Assert.Same(expected, actual)|};
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int expected, int actual)
                {
                    Assert.Equal(expected, actual);
                }
            }
            """;

        await CreateCodeFixTest<SameWithValueTypeAnalyzerType, SameWithValueTypeCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertNotSameWithValueType()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int expected, int actual)
                {
                    {|MFAS0011:Assert.NotSame(expected, actual)|};
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int expected, int actual)
                {
                    Assert.NotEqual(expected, actual);
                }
            }
            """;

        await CreateCodeFixTest<SameWithValueTypeAnalyzerType, SameWithValueTypeCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }
}
