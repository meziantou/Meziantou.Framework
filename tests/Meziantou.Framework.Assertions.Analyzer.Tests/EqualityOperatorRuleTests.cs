using EqualityOperatorAnalyzerType = Meziantou.Framework.Analyzers.Assertions.EqualityOperatorAnalyzer;
using EqualityOperatorCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.EqualityOperatorCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class EqualityOperatorRuleTests : AssertionsAnalyzerTestBase
{
    [Theory]
    [InlineData("Assert.True({|MFAS0031:value == 42|});", "Assert.Equal(42, value);")]
    [InlineData("Assert.True({|MFAS0031:42 == value|});", "Assert.Equal(42, value);")]
    [InlineData("Assert.True({|MFAS0032:value != 42|});", "Assert.NotEqual(42, value);")]
    [InlineData("Assert.False({|MFAS0032:value == 42|});", "Assert.NotEqual(42, value);")]
    [InlineData("Assert.False({|MFAS0031:value != 42|});", "Assert.Equal(42, value);")]
    [InlineData("Assert.True({|MFAS0031:value == other|});", "Assert.Equal(value, other);")]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForIntComparison(string assertion, string fixedAssertion)
    {
        var source = $$"""
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int value, int other)
                {
                    {{assertion}}
                }
            }
            """;

        var fixedSource = $$"""
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int value, int other)
                {
                    {{fixedAssertion}}
                }
            }
            """;

        await CreateCodeFixTest<EqualityOperatorAnalyzerType, EqualityOperatorCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForStringComparison()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.True({|MFAS0031:value == "expected"|});
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.Equal("expected", value);
                }
            }
            """;

        await CreateCodeFixTest<EqualityOperatorAnalyzerType, EqualityOperatorCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForEnumComparison()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public enum Color { Red, Green }

            public static class TestClass
            {
                public static void M(Color value)
                {
                    Assert.True({|MFAS0031:value == Color.Red|});
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public enum Color { Red, Green }

            public static class TestClass
            {
                public static void M(Color value)
                {
                    Assert.Equal(Color.Red, value);
                }
            }
            """;

        await CreateCodeFixTest<EqualityOperatorAnalyzerType, EqualityOperatorCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_PreservesMessage()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int value)
                {
                    Assert.True({|MFAS0031:value == 42|}, "custom message");
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
                    Assert.Equal(42, value, message: "custom message");
                }
            }
            """;

        await CreateCodeFixTest<EqualityOperatorAnalyzerType, EqualityOperatorCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForNullComparisonAndReferenceTypes()
    {
        var source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public sealed class Sample
            {
                public static bool operator ==(Sample? a, Sample? b) => ReferenceEquals(a, b);
                public static bool operator !=(Sample? a, Sample? b) => !ReferenceEquals(a, b);
                public override bool Equals(object? obj) => true;
                public override int GetHashCode() => 0;
            }

            public static class TestClass
            {
                public static void M(string? text, Sample? sample, List<int> collection, double number)
                {
                    // Null comparisons belong to MFAS0006/MFAS0007
                    Assert.True(text == null);
                    Assert.False(text != null);

                    // User-defined operators may not agree with Assert.Equal
                    Assert.True(sample == null);

                    // Count comparisons belong to MFAS0004/MFAS0005
                    Assert.True(collection.Count == 3);

                    // double.NaN == double.NaN is false but double.NaN.Equals(double.NaN) is true
                    Assert.True(number == 1.5);
                }
            }
            """;

        await CreateAnalyzerTest<EqualityOperatorAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForNullableValue()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int? value)
                {
                    Assert.True({|MFAS0031:value == 42|});
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int? value)
                {
                    Assert.Equal(42, value);
                }
            }
            """;

        await CreateCodeFixTest<EqualityOperatorAnalyzerType, EqualityOperatorCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ThroughTypeAlias()
    {
        var source = """
            using MyAssert = Meziantou.Framework.Assertions.Assert;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int value)
                {
                    MyAssert.True({|MFAS0031:value == 42|});
                }
            }
            """;

        var fixedSource = """
            using MyAssert = Meziantou.Framework.Assertions.Assert;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int value)
                {
                    MyAssert.Equal(42, value);
                }
            }
            """;

        await CreateCodeFixTest<EqualityOperatorAnalyzerType, EqualityOperatorCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ThroughUsingStatic()
    {
        var source = """
            using static Meziantou.Framework.Assertions.Assert;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int value)
                {
                    True({|MFAS0031:value == 42|});
                }
            }
            """;

        var fixedSource = """
            using static Meziantou.Framework.Assertions.Assert;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int value)
                {
                    Equal(42, value);
                }
            }
            """;

        await CreateCodeFixTest<EqualityOperatorAnalyzerType, EqualityOperatorCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }
}
