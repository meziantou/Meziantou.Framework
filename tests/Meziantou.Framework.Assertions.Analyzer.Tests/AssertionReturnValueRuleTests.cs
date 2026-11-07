using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.Testing;
using AssertionReturnValueAnalyzerType = Meziantou.Framework.Analyzers.Assertions.AssertionReturnValueAnalyzer;
using AssertionReturnValueCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.AssertionReturnValueCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed partial class AssertionReturnValueRuleTests : AssertionsAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForSingleWithIndexer()
    {
        var source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> collection)
                {
                    {|#0:Assert.Single(collection)|};
                    Assert.Equal(1, {|#1:collection[0]|});
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> collection)
                {
                    var item = Assert.Single(collection);
                    Assert.Equal(1, item);
                }
            }
            """;

        await VerifyCodeFixAsync(source, fixedSource);
    }

    [Theory]
    [InlineData("collection.Single()")]
    [InlineData("collection.SingleOrDefault()")]
    [InlineData("collection.First()")]
    [InlineData("collection.FirstOrDefault()")]
    [InlineData("collection.Last()")]
    [InlineData("collection.LastOrDefault()")]
    [InlineData("collection.ElementAt(0)")]
    [InlineData("Enumerable.First(collection)")]
    public async Task Analyzer_ReportDiagnostic_ForSingleWithLinq(string rederivation)
    {
        var source = $$"""
            using System.Collections.Generic;
            using System.Linq;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(IEnumerable<string> collection)
                {
                    {|#0:Assert.Single(collection)|};
                    Assert.Equal("a", {|#1:{{rederivation}}|});
                }
            }
            """;

        await VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_ForSingleWithArray()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string[] collection)
                {
                    {|#0:Assert.Single(collection)|};
                    Assert.Equal("a", {|#1:collection[0]|});
                }
            }
            """;

        await VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForSingleWithString()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string text)
                {
                    {|#0:Assert.Single(text)|};
                    Assert.Equal('a', {|#1:text[0]|});
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(string text)
                {
                    var item = Assert.Single(text);
                    Assert.Equal('a', item);
                }
            }
            """;

        await VerifyCodeFixAsync(source, fixedSource);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForIsTypeWithCast()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public class Base { }
            public sealed class Derived : Base { public string Name { get; set; } = ""; }

            public static class TestClass
            {
                public static void M(Base value)
                {
                    {|#0:Assert.IsType<Derived>(value)|};
                    Assert.Equal("a", ({|#1:(Derived)value|}).Name);
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public class Base { }
            public sealed class Derived : Base { public string Name { get; set; } = ""; }

            public static class TestClass
            {
                public static void M(Base value)
                {
                    var derived = Assert.IsType<Derived>(value);
                    Assert.Equal("a", derived.Name);
                }
            }
            """;

        await VerifyCodeFixAsync(source, fixedSource);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForIsTypeWithAsOperator()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(object value)
                {
                    {|#0:Assert.IsType<string>(value)|};
                    Assert.StartsWith("Hello", {|#1:value as string|});
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
                    var value2 = Assert.IsType<string>(value);
                    Assert.StartsWith("Hello", value2);
                }
            }
            """;

        await VerifyCodeFixAsync(source, fixedSource);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForIsAssignableToWithInterface()
    {
        var source = """
            using System;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(object instance)
                {
                    {|#0:Assert.IsAssignableTo<IDisposable>(instance)|};
                    ({|#1:(IDisposable)instance|}).Dispose();
                }
            }
            """;

        var fixedSource = """
            using System;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(object instance)
                {
                    var disposable = Assert.IsAssignableTo<IDisposable>(instance);
                    disposable.Dispose();
                }
            }
            """;

        await VerifyCodeFixAsync(source, fixedSource);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_ForIsTypeWithUnboxing()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(object value)
                {
                    {|#0:Assert.IsType<int>(value)|};
                    Assert.Equal(1, {|#1:(int)value|});
                }
            }
            """;

        await VerifyAnalyzerAsync(source);
    }

    [Theory]
    [InlineData("number.Value")]
    [InlineData("number.GetValueOrDefault()")]
    [InlineData("(int)number")]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForNotNullWithNullableValueType(string rederivation)
    {
        var source = $$"""
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int? number)
                {
                    {|#0:Assert.NotNull(number)|};
                    Assert.Equal(1, {|#1:{{rederivation}}|});
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(int? number)
                {
                    var value = Assert.NotNull(number);
                    Assert.Equal(1, value);
                }
            }
            """;

        await VerifyCodeFixAsync(source, fixedSource);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForContainsWithDictionaryIndexer()
    {
        var source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(Dictionary<string, int> dictionary)
                {
                    {|#0:Assert.Contains("key", dictionary)|};
                    Assert.Equal(1, {|#1:dictionary["key"]|});
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(Dictionary<string, int> dictionary)
                {
                    var value = Assert.Contains("key", dictionary);
                    Assert.Equal(1, value);
                }
            }
            """;

        await VerifyCodeFixAsync(source, fixedSource);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_ForContainsWithReadOnlyDictionaryAndLocalKey()
    {
        var source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(IReadOnlyDictionary<string, int> dictionary, string key)
                {
                    {|#0:Assert.Contains(key, dictionary)|};
                    Assert.Equal(1, {|#1:dictionary[key]|});
                }
            }
            """;

        await VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ReplacesAllRederivations()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public sealed class Item { public string Name { get; set; } = ""; public int Age { get; set; } }

            public sealed class TestClass
            {
                private Item[] _items = [];

                public void M(bool condition)
                {
                    {|#0:Assert.Single(_items)|};
                    Assert.Equal("a", {|#1:_items[0]|}.Name);
                    if (condition)
                    {
                        Assert.Equal(1, {|#2:this._items[0]|}.Age);
                    }
                }
            }
            """;

        var fixedSource = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public sealed class Item { public string Name { get; set; } = ""; public int Age { get; set; } }

            public sealed class TestClass
            {
                private Item[] _items = [];

                public void M(bool condition)
                {
                    var item = Assert.Single(_items);
                    Assert.Equal("a", item.Name);
                    if (condition)
                    {
                        Assert.Equal(1, item.Age);
                    }
                }
            }
            """;

        await VerifyCodeFixAsync(source, fixedSource);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_UsesUniqueNames()
    {
        var source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> first, List<int> second)
                {
                    {|#0:Assert.Single(first)|};
                    Assert.Equal(1, {|#1:first[0]|});
                    {|#2:Assert.Single(second)|};
                    Assert.Equal(2, {|#3:second[0]|});
                    foreach (var item3 in second)
                    {
                    }
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> first, List<int> second)
                {
                    var item = Assert.Single(first);
                    Assert.Equal(1, item);
                    var item2 = Assert.Single(second);
                    Assert.Equal(2, item2);
                    foreach (var item3 in second)
                    {
                    }
                }
            }
            """;

        var test = CreateCodeFixTest<AssertionReturnValueAnalyzerType, AssertionReturnValueCodeFixProviderType>(source, fixedSource);
        test.ExpectedDiagnostics.Add(CreateDiagnosticResult().WithLocation(0).WithLocation(1));
        test.ExpectedDiagnostics.Add(CreateDiagnosticResult().WithLocation(2).WithLocation(3));
        await test.RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_AvoidsNamesDeclaredLater()
    {
        var source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> collection)
                {
                    {|#0:Assert.Single(collection)|};
                    Assert.Equal(1, {|#1:collection[0]|});
                    var item = 0;
                    _ = item;
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> collection)
                {
                    var item2 = Assert.Single(collection);
                    Assert.Equal(1, item2);
                    var item = 0;
                    _ = item;
                }
            }
            """;

        await VerifyCodeFixAsync(source, fixedSource);
    }

    [Fact]
    public async Task Analyzer_MessageIndicatesTheAssertionAndTheRederivation()
    {
        var source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> collection)
                {
                    {|#0:Assert.Single(collection)|};
                    Assert.Equal(1, {|#1:collection[0]|});
                }
            }
            """;

        var test = CreateAnalyzerTest<AssertionReturnValueAnalyzerType>(source);
        test.ExpectedDiagnostics.Add(new DiagnosticResult(AssertionReturnValueAnalyzerType.Descriptor)
            .WithLocation(0)
            .WithLocation(1)
            .WithMessage("Use the value returned by Assert.Single instead of re-deriving it with 'collection[0]'"));

        await test.RunAsync(XunitCancellationToken);
    }

    [Theory]
    [InlineData("var item = Assert.Single(collection); Assert.Equal(1, collection[0]);")]
    [InlineData("Assert.Single(collection); Assert.Equal(1, collection[1]);")]
    [InlineData("Assert.Single(collection); Assert.Equal(1, other[0]);")]
    [InlineData("Assert.Single(collection, x => x > 0); Assert.Equal(1, collection[0]);")]
    [InlineData("Assert.Single(collection); collection = other; Assert.Equal(1, collection[0]);")]
    [InlineData("Assert.Single(collection); collection.Insert(0, 2); Assert.Equal(1, collection[0]);")]
    [InlineData("Assert.Single(collection); collection[0] = 2; Assert.Equal(2, collection[0]);")]
    [InlineData("Assert.Single(collection); Replace(ref collection); Assert.Equal(1, collection[0]);")]
    [InlineData("Assert.Single(collection); Assert.Equal(1, collection.First(x => x > 0));")]
    [InlineData("Assert.Single(collection); Func<int> f = () => collection[0];")]
    [InlineData("Assert.Single(GetItems()); Assert.Equal(1, GetItems()[0]);")]
    [InlineData("if (collection.Count > 0) Assert.Single(collection); Assert.Equal(1, collection[0]);")]
    [InlineData("Assert.Equal(1, collection[0]); Assert.Single(collection);")]
    public async Task Analyzer_NoDiagnostic_ForSingle(string statements)
    {
        var source = $$"""
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(List<int> collection, List<int> other)
                {
                    {{statements}}
                }

                private static List<int> GetItems() => [];
                private static void Replace(ref List<int> value) { }
            }
            """;

        await VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_WhenValueTypeArrayElementIsRead()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public struct Point { public int X; }

            public static class TestClass
            {
                public static void M(Point[] points)
                {
                    {|#0:Assert.Single(points)|};
                    Assert.Equal(1, {|#1:points[0]|}.X);
                }
            }
            """;

        await VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Analyzer_NoDiagnostic_WhenValueTypeArrayElementIsModified()
    {
        var source = """
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public struct Point { public int X; }

            public static class TestClass
            {
                public static void M(Point[] points)
                {
                    Assert.Single(points);
                    points[0].X = 1;
                }
            }
            """;

        await VerifyAnalyzerAsync(source);
    }

    [Theory]
    [InlineData("Assert.IsType<Derived>(value); _ = (Base)value;")]
    [InlineData("Assert.IsType(typeof(Derived), value); _ = (Derived)value;")]
    [InlineData("Assert.IsType<Derived>(value); _ = (Converted)value;")]
    [InlineData("Assert.IsType<Derived>(value); Derived d = (Derived)other;")]
    [InlineData("Assert.IsType<Derived>(value); value = other; _ = (Derived)value;")]
    [InlineData("Assert.NotNull(value); _ = (Derived)value;")]
    [InlineData("Assert.NotNull(value); _ = (Converted)value;")]
    public async Task Analyzer_NoDiagnostic_ForIsType(string statements)
    {
        var source = $$"""
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public class Base { }
            public sealed class Derived : Base { }
            public sealed class Converted { public static explicit operator Converted(Base value) => new(); }

            public static class TestClass
            {
                public static void M(Base value, Base other)
                {
                    {{statements}}
                }
            }
            """;

        await VerifyAnalyzerAsync(source);
    }

    [Theory]
    [InlineData("""Assert.Contains("key", dictionary); Assert.Equal(1, dictionary["other"]);""")]
    [InlineData("""Assert.Contains("key", dictionary, StringComparer.OrdinalIgnoreCase); Assert.Equal(1, dictionary["key"]);""")]
    [InlineData("""Assert.Contains("key", dictionary); dictionary["key"] = 2; Assert.Equal(2, dictionary["key"]);""")]
    [InlineData("""Assert.Contains("key", dictionary); dictionary.Remove("key"); Assert.Equal(1, dictionary["key"]);""")]
    public async Task Analyzer_NoDiagnostic_ForContains(string statements)
    {
        var source = $$"""
            using System;
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(IDictionary<string, int> dictionary)
                {
                    {{statements}}
                }
            }
            """;

        await VerifyAnalyzerAsync(source);
    }

    private static async Task VerifyAnalyzerAsync(string source)
    {
        var test = CreateAnalyzerTest<AssertionReturnValueAnalyzerType>(source);
        AddExpectedDiagnostic(test.ExpectedDiagnostics, source);
        await test.RunAsync(XunitCancellationToken);
    }

    private static async Task VerifyCodeFixAsync(string source, string fixedSource)
    {
        var test = CreateCodeFixTest<AssertionReturnValueAnalyzerType, AssertionReturnValueCodeFixProviderType>(source, fixedSource);
        AddExpectedDiagnostic(test.ExpectedDiagnostics, source);
        await test.RunAsync(XunitCancellationToken);
    }

    // The diagnostic is reported on the assertion ({|#0:|}), with the re-derivations ({|#1:|}, ...) as additional locations
    private static void AddExpectedDiagnostic(List<DiagnosticResult> expectedDiagnostics, string source)
    {
        var locationCount = LocationMarkupRegex().Matches(source).Count;
        if (locationCount == 0)
            return;

        var diagnostic = CreateDiagnosticResult();
        for (var i = 0; i < locationCount; i++)
        {
            diagnostic = diagnostic.WithLocation(i);
        }

        expectedDiagnostics.Add(diagnostic);
    }

    private static DiagnosticResult CreateDiagnosticResult()
    {
        return new DiagnosticResult(AssertionReturnValueAnalyzerType.Descriptor.Id, AssertionReturnValueAnalyzerType.Descriptor.DefaultSeverity);
    }

    [GeneratedRegex(@"\{\|#[0-9]+:", RegexOptions.None, matchTimeoutMilliseconds: -1)]
    private static partial Regex LocationMarkupRegex();
}
