using IsTypeAnalyzerType = Meziantou.Framework.Analyzers.Assertions.IsTypeAnalyzer;

namespace Meziantou.Framework.Tests;

public sealed class IsTypeRuleTests : AssertionsAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_ForIsTypeWithStaticType()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class SampleType
            {
            }

            public static class TestClass
            {
                public static void M(object actual)
                {
                    {|MFAS0003:Assert.IsType(typeof(SampleType), actual)|};
                }
            }
            """;

        await CreateAnalyzerTest<IsTypeAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_ForIsTypeWithAbstractType()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public abstract class SampleType
            {
            }

            public static class TestClass
            {
                public static void M(object actual)
                {
                    {|MFAS0003:Assert.IsType<SampleType>(actual)|};
                }
            }
            """;

        await CreateAnalyzerTest<IsTypeAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForConcreteType()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public class SampleType
            {
            }

            public static class TestClass
            {
                public static void M(object actual)
                {
                    _ = Assert.IsType<SampleType>(actual);
                    _ = Assert.IsType(typeof(SampleType), actual);
                }
            }
            """;

        await CreateAnalyzerTest<IsTypeAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
