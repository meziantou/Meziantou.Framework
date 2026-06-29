using UseNullAssertionAnalyzerType = Meziantou.Framework.Analyzers.Assertions.UseNullAssertionAnalyzer;
using UseNullAssertionCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.UseNullAssertionCodeFixProvider;
using NullWithValueTypeAnalyzerType = Meziantou.Framework.Analyzers.Assertions.NullWithValueTypeAnalyzer;
using NullWithValueTypeCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.NullWithValueTypeCodeFixProvider;
using SameWithValueTypeAnalyzerType = Meziantou.Framework.Analyzers.Assertions.SameWithValueTypeAnalyzer;
using SameWithValueTypeCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.SameWithValueTypeCodeFixProvider;
using IsAssignableToAnalyzerType = Meziantou.Framework.Analyzers.Assertions.IsAssignableToAnalyzer;
using IsAssignableToCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.IsAssignableToCodeFixProvider;
using RegexMatchesAnalyzerType = Meziantou.Framework.Analyzers.Assertions.RegexMatchesAnalyzer;
using RegexMatchesCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.RegexMatchesCodeFixProvider;
using StringContainsAnalyzerType = Meziantou.Framework.Analyzers.Assertions.StringContainsAnalyzer;
using StringContainsCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.StringContainsCodeFixProvider;
using StringStartsWithAnalyzerType = Meziantou.Framework.Analyzers.Assertions.StringStartsWithAnalyzer;
using StringStartsWithCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.StringStartsWithCodeFixProvider;
using StringEndsWithAnalyzerType = Meziantou.Framework.Analyzers.Assertions.StringEndsWithAnalyzer;
using StringEndsWithCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.StringEndsWithCodeFixProvider;
using CollectionContainsAnalyzerType = Meziantou.Framework.Analyzers.Assertions.CollectionContainsAnalyzer;
using CollectionContainsCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.CollectionContainsCodeFixProvider;
using CollectionAnyAnalyzerType = Meziantou.Framework.Analyzers.Assertions.CollectionAnyAnalyzer;
using CollectionAnyCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.CollectionAnyCodeFixProvider;
using CollectionAllAnalyzerType = Meziantou.Framework.Analyzers.Assertions.CollectionAllAnalyzer;
using CollectionAllCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.CollectionAllCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class AssertionMethodSelectionRuleTests : AssertionsAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertTrueEqualsNull()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string? value)
                {
                    Assert.True({|MFAS0006:value|} == null);
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string? value)
                {
                    Assert.Null(value);
                }
            }
            """;

        await CreateCodeFixTest<UseNullAssertionAnalyzerType, UseNullAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertTrueNotEqualsNull()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string? value)
                {
                    Assert.True({|MFAS0007:value|} != null);
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string? value)
                {
                    Assert.NotNull(value);
                }
            }
            """;

        await CreateCodeFixTest<UseNullAssertionAnalyzerType, UseNullAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertTrueIsNull()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string? value)
                {
                    Assert.True({|MFAS0006:value|} is null);
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string? value)
                {
                    Assert.Null(value);
                }
            }
            """;

        await CreateCodeFixTest<UseNullAssertionAnalyzerType, UseNullAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertTrueIsNotNull()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string? value)
                {
                    Assert.True({|MFAS0007:value|} is not null);
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string? value)
                {
                    Assert.NotNull(value);
                }
            }
            """;

        await CreateCodeFixTest<UseNullAssertionAnalyzerType, UseNullAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

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

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertTrueStringContains()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.True({|MFAS0016:value.Contains("abc")|});
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
                    Assert.Contains("abc", value);
                }
            }
            """;

        await CreateCodeFixTest<StringContainsAnalyzerType, StringContainsCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertFalseStringContains()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.False({|MFAS0017:value.Contains("abc")|});
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
                    Assert.DoesNotContain("abc", value);
                }
            }
            """;

        await CreateCodeFixTest<StringContainsAnalyzerType, StringContainsCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertTrueStringContainsWithOrdinalIgnoreCase()
    {
        var source = """
            using System;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.True({|MFAS0016:value.Contains("abc", StringComparison.OrdinalIgnoreCase)|});
                }
            }
            """;

        var fixedSource = """
            using System;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.Contains("abc", value, true);
                }
            }
            """;

        await CreateCodeFixTest<StringContainsAnalyzerType, StringContainsCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertTrueStringContainsWithOrdinal()
    {
        var source = """
            using System;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.True({|MFAS0016:value.Contains("abc", StringComparison.Ordinal)|});
                }
            }
            """;

        var fixedSource = """
            using System;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.Contains("abc", value);
                }
            }
            """;

        await CreateCodeFixTest<StringContainsAnalyzerType, StringContainsCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_ForAssertTrueStringContainsWithCurrentCulture()
    {
        var source = """
            using System;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.True(value.Contains("abc", StringComparison.CurrentCulture));
                }
            }
            """;

        await CreateAnalyzerTest<StringContainsAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertTrueStringStartsWith()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.True({|MFAS0018:value.StartsWith("abc")|});
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
                    Assert.StartsWith("abc", value);
                }
            }
            """;

        await CreateCodeFixTest<StringStartsWithAnalyzerType, StringStartsWithCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertFalseStringStartsWith()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.False({|MFAS0019:value.StartsWith("abc")|});
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
                    Assert.DoesNotStartWith("abc", value);
                }
            }
            """;

        await CreateCodeFixTest<StringStartsWithAnalyzerType, StringStartsWithCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertTrueStringStartsWithIgnoreCase()
    {
        var source = """
            using System;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.True({|MFAS0018:value.StartsWith("abc", StringComparison.OrdinalIgnoreCase)|});
                }
            }
            """;

        var fixedSource = """
            using System;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.StartsWith("abc", value, true);
                }
            }
            """;

        await CreateCodeFixTest<StringStartsWithAnalyzerType, StringStartsWithCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertTrueStringStartsWithIgnoreCaseBool()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.True({|MFAS0018:value.StartsWith("abc", ignoreCase: true, culture: null)|});
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
                    Assert.StartsWith("abc", value, true);
                }
            }
            """;

        await CreateCodeFixTest<StringStartsWithAnalyzerType, StringStartsWithCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertTrueStringEndsWith()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.True({|MFAS0020:value.EndsWith("abc")|});
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
                    Assert.EndsWith("abc", value);
                }
            }
            """;

        await CreateCodeFixTest<StringEndsWithAnalyzerType, StringEndsWithCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertFalseStringEndsWith()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string value)
                {
                    Assert.False({|MFAS0021:value.EndsWith("abc")|});
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
                    Assert.DoesNotEndWith("abc", value);
                }
            }
            """;

        await CreateCodeFixTest<StringEndsWithAnalyzerType, StringEndsWithCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertTrueCollectionContains()
    {
        var source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> collection, int item)
                {
                    Assert.True({|MFAS0022:collection.Contains(item)|});
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> collection, int item)
                {
                    Assert.Contains(item, collection);
                }
            }
            """;

        await CreateCodeFixTest<CollectionContainsAnalyzerType, CollectionContainsCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertFalseCollectionContains()
    {
        var source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> collection, int item)
                {
                    Assert.False({|MFAS0023:collection.Contains(item)|});
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> collection, int item)
                {
                    Assert.DoesNotContain(item, collection);
                }
            }
            """;

        await CreateCodeFixTest<CollectionContainsAnalyzerType, CollectionContainsCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertTrueDictionaryContainsKey()
    {
        var source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(Dictionary<string, int> dict, string key)
                {
                    Assert.True({|MFAS0022:dict.ContainsKey(key)|});
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(Dictionary<string, int> dict, string key)
                {
                    Assert.Contains(key, dict);
                }
            }
            """;

        await CreateCodeFixTest<CollectionContainsAnalyzerType, CollectionContainsCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertFalseDictionaryContainsKey()
    {
        var source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(Dictionary<string, int> dict, string key)
                {
                    Assert.False({|MFAS0023:dict.ContainsKey(key)|});
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(Dictionary<string, int> dict, string key)
                {
                    Assert.DoesNotContain(key, dict);
                }
            }
            """;

        await CreateCodeFixTest<CollectionContainsAnalyzerType, CollectionContainsCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertTrueCollectionAny()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> collection)
                {
                    Assert.True({|MFAS0024:collection.Any(x => x > 0)|});
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Generic;
            using System.Linq;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> collection)
                {
                    Assert.Contains(collection, x => x > 0);
                }
            }
            """;

        await CreateCodeFixTest<CollectionAnyAnalyzerType, CollectionAnyCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertFalseCollectionAny()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> collection)
                {
                    Assert.False({|MFAS0025:collection.Any(x => x > 0)|});
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Generic;
            using System.Linq;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> collection)
                {
                    Assert.DoesNotContain(collection, x => x > 0);
                }
            }
            """;

        await CreateCodeFixTest<CollectionAnyAnalyzerType, CollectionAnyCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertTrueCollectionAll()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> collection)
                {
                    Assert.True({|MFAS0026:collection.All(x => x > 0)|});
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Generic;
            using System.Linq;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> collection)
                {
                    Assert.All(collection, x => x > 0);
                }
            }
            """;

        await CreateCodeFixTest<CollectionAllAnalyzerType, CollectionAllCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForAssertFalseCollectionAll()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> collection)
                {
                    Assert.False({|MFAS0027:collection.All(x => x > 0)|});
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Generic;
            using System.Linq;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> collection)
                {
                    Assert.DoesNotAll(collection, x => x > 0);
                }
            }
            """;

        await CreateCodeFixTest<CollectionAllAnalyzerType, CollectionAllCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }
}
