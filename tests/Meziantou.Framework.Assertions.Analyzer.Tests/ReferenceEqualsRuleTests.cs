using ReferenceEqualsAnalyzerType = Meziantou.Framework.Analyzers.Assertions.ReferenceEqualsAnalyzer;
using ReferenceEqualsCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.ReferenceEqualsCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class ReferenceEqualsRuleTests : AssertionsAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForReferenceEquals()
    {
        var source = """
                namespace Meziantou.Framework.Assertions
                {
                    public static class Assert
                    {
                        public static bool ReferenceEquals(object? a, object? b) => false;
                        public static void Same(object? expected, object? actual) { }
                    }
                }

                namespace Sample
                {
                    using Meziantou.Framework.Assertions;

                    public static class TestClass
                    {
                        public static void M(object expected, object actual)
                        {
                            {|MFAS0002:Assert.ReferenceEquals(expected, actual)|};
                        }
                    }
                }
                """;

        var fixedSource = """
                namespace Meziantou.Framework.Assertions
                {
                    public static class Assert
                    {
                        public static bool ReferenceEquals(object? a, object? b) => false;
                        public static void Same(object? expected, object? actual) { }
                    }
                }

                namespace Sample
                {
                    using Meziantou.Framework.Assertions;

                    public static class TestClass
                    {
                        public static void M(object expected, object actual)
                        {
                            Assert.Same(expected, actual);
                        }
                    }
                }
                """;

        await CreateCodeFixTest<ReferenceEqualsAnalyzerType, ReferenceEqualsCodeFixProviderType>(source, fixedSource, addAssertionsReference: false).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForNamedArguments()
    {
        var source = """
                namespace Meziantou.Framework.Assertions
                {
                    public static class Assert
                    {
                        public static bool ReferenceEquals(object? a, object? b) => false;
                        public static void Same(object? expected, object? actual) { }
                    }
                }

                namespace Sample
                {
                    using Meziantou.Framework.Assertions;

                    public static class TestClass
                    {
                        public static void M(object expected, object actual)
                        {
                            {|MFAS0002:Assert.ReferenceEquals(a: expected, b: actual)|};
                        }
                    }
                }
                """;

        var fixedSource = """
                namespace Meziantou.Framework.Assertions
                {
                    public static class Assert
                    {
                        public static bool ReferenceEquals(object? a, object? b) => false;
                        public static void Same(object? expected, object? actual) { }
                    }
                }

                namespace Sample
                {
                    using Meziantou.Framework.Assertions;

                    public static class TestClass
                    {
                        public static void M(object expected, object actual)
                        {
                            Assert.Same(expected: expected, actual: actual);
                        }
                    }
                }
                """;

        await CreateCodeFixTest<ReferenceEqualsAnalyzerType, ReferenceEqualsCodeFixProviderType>(source, fixedSource, addAssertionsReference: false).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_ForReferenceEqualsInExpression()
    {
        var source = """
            namespace Meziantou.Framework.Assertions
            {
                public static class Assert
                {
                    public static bool ReferenceEquals(object? a, object? b) => false;
                    public static void Same(object? expected, object? actual) { }
                }
            }

            namespace Sample
            {
                using Meziantou.Framework.Assertions;

                public static class TestClass
                {
                    public static bool M(object expected, object actual)
                    {
                        return {|MFAS0002:Assert.ReferenceEquals(expected, actual)|};
                    }
                }
            }
            """;

        await CreateAnalyzerTest<ReferenceEqualsAnalyzerType>(source, addAssertionsReference: false).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForSameOrObjectReferenceEquals()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static bool M(object expected, object actual)
                {
                    Assert.Same(expected, actual);
                    return object.ReferenceEquals(expected, actual);
                }
            }
            """;

        await CreateAnalyzerTest<ReferenceEqualsAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
