using System.Reflection;
using Microsoft.CodeAnalysis;
using NullWithValueTypeAnalyzerType = Meziantou.Framework.Analyzers.Assertions.NullWithValueTypeAnalyzer;
using NullWithValueTypeCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.NullWithValueTypeCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class NullWithValueTypeRuleTests : AssertionsAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertNullWithValueType()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int value)
                {
                    Assert.Null({|MFAS0008:value|});
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int value)
                {
                    Assert.Equal(default(int), value);
                }
            }
            """;

        await CreateCodeFixTest<NullWithValueTypeAnalyzerType, NullWithValueTypeCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertNotNullWithValueType()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int value)
                {
                    Assert.NotNull({|MFAS0009:value|});
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int value)
                {
                    Assert.NotEqual(default(int), value);
                }
            }
            """;

        await CreateCodeFixTest<NullWithValueTypeAnalyzerType, NullWithValueTypeCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForAssertNotNullWithUnion()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(Meziantou.Framework.Tests.NullWithValueTypeRuleTests.SampleUnionForAnalyzerTest value)
                {
                    Assert.NotNull(value);
                }
            }
            """;

        var test = CreateAnalyzerTest<NullWithValueTypeAnalyzerType>(source);
        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(Assembly.GetExecutingAssembly().Location));

        await test.RunAsync(XunitCancellationToken);
    }

    public union SampleUnionForAnalyzerTest(string?, int);
}
