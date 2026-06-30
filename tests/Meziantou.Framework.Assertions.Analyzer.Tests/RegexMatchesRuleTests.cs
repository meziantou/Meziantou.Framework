using RegexMatchesAnalyzerType = Meziantou.Framework.Analyzers.Assertions.RegexMatchesAnalyzer;
using RegexMatchesCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.RegexMatchesCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class RegexMatchesRuleTests : AssertionsAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertTrueRegexIsMatch()
    {
        var source = """
            using System.Text.RegularExpressions;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.True({|MFAS0014:Regex.IsMatch(value, @"\d+")|});
                }
            }
            """;

        var fixedSource = """
            using System.Text.RegularExpressions;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.Matches(@"\d+", value);
                }
            }
            """;

        await CreateCodeFixTest<RegexMatchesAnalyzerType, RegexMatchesCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertFalseRegexIsMatch()
    {
        var source = """
            using System.Text.RegularExpressions;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.False({|MFAS0015:Regex.IsMatch(value, @"\d+")|});
                }
            }
            """;

        var fixedSource = """
            using System.Text.RegularExpressions;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.DoesNotMatch(@"\d+", value);
                }
            }
            """;

        await CreateCodeFixTest<RegexMatchesAnalyzerType, RegexMatchesCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }
}
