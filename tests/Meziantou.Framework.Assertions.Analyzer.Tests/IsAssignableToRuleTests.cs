using IsAssignableToAnalyzerType = Meziantou.Framework.Analyzers.Assertions.IsAssignableToAnalyzer;
using IsAssignableToCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.IsAssignableToCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class IsAssignableToRuleTests : AssertionsAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertTrueIsType()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(object value)
                {
                    Assert.True({|MFAS0012:value|} is string);
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(object value)
                {
                    Assert.IsAssignableTo<string>(value);
                }
            }
            """;

        await CreateCodeFixTest<IsAssignableToAnalyzerType, IsAssignableToCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertFalseIsType()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(object value)
                {
                    Assert.False({|MFAS0013:value|} is string);
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(object value)
                {
                    Assert.IsNotAssignableTo<string>(value);
                }
            }
            """;

        await CreateCodeFixTest<IsAssignableToAnalyzerType, IsAssignableToCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertTrueIsNotType()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(object value)
                {
                    Assert.True({|MFAS0013:value|} is not string);
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(object value)
                {
                    Assert.IsNotAssignableTo<string>(value);
                }
            }
            """;

        await CreateCodeFixTest<IsAssignableToAnalyzerType, IsAssignableToCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertFalseIsNotType()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(object value)
                {
                    Assert.False({|MFAS0012:value|} is not string);
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(object value)
                {
                    Assert.IsAssignableTo<string>(value);
                }
            }
            """;

        await CreateCodeFixTest<IsAssignableToAnalyzerType, IsAssignableToCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }
}
