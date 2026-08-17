using AwaitAssertionAnalyzerType = Meziantou.Framework.Analyzers.Assertions.AwaitAssertionAnalyzer;
using AwaitAssertionCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.AwaitAssertionCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class AwaitAssertionRuleTests : AssertionsAnalyzerTestBase
{
    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForDiscardedTaskInSyncMethod()
    {
        var source = """
            using System;
            using System.Threading.Tasks;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(Func<Task> action)
                {
                    {|MFAS0048:Assert.ThrowsAsync<InvalidOperationException>(action)|};
                }
            }
            """;

        var fixedSource = """
            using System;
            using System.Threading.Tasks;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static async Task M(Func<Task> action)
                {
                    await Assert.ThrowsAsync<InvalidOperationException>(action);
                }
            }
            """;

        await CreateCodeFixTest<AwaitAssertionAnalyzerType, AwaitAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForDiscardedTaskInAsyncMethod()
    {
        var source = """
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static async Task M(IAsyncEnumerable<int> actual)
                {
                    {|MFAS0048:Assert.Empty(actual)|};
                    await Task.Yield();
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static async Task M(IAsyncEnumerable<int> actual)
                {
                    await Assert.Empty(actual);
                    await Task.Yield();
                }
            }
            """;

        await CreateCodeFixTest<AwaitAssertionAnalyzerType, AwaitAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForExpressionBodiedMethod()
    {
        var source = """
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(IAsyncEnumerable<int> actual) => {|MFAS0048:Assert.Empty(actual)|};
            }
            """;

        var fixedSource = """
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static async Task M(IAsyncEnumerable<int> actual) => await Assert.Empty(actual);
            }
            """;

        await CreateCodeFixTest<AwaitAssertionAnalyzerType, AwaitAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_InsideAsyncLambda()
    {
        var source = """
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(IAsyncEnumerable<int> actual)
                {
                    Func<Task> f = async () => {|MFAS0048:Assert.Empty(actual)|};
                    _ = f;
                }
            }
            """;

        var fixedSource = """
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(IAsyncEnumerable<int> actual)
                {
                    Func<Task> f = async () => await Assert.Empty(actual);
                    _ = f;
                }
            }
            """;

        await CreateCodeFixTest<AwaitAssertionAnalyzerType, AwaitAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportsDiagnostic_ButOffersNoFix_InsideNonAsyncLambda()
    {
        // Adding async to the lambda would change the delegate type it is bound to, so no fix is offered
        var source = """
            using System;
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(IAsyncEnumerable<int> actual)
                {
                    Action a = () => {|MFAS0048:Assert.Empty(actual)|};
                    a();
                }
            }
            """;

        await CreateCodeFixTest<AwaitAssertionAnalyzerType, AwaitAssertionCodeFixProviderType>(source, source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportsDiagnostic_ButOffersNoFix_InsideNonAsyncLocalFunction()
    {
        var source = """
            using System.Collections.Generic;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(IAsyncEnumerable<int> actual)
                {
                    Local();

                    void Local() => {|MFAS0048:Assert.Empty(actual)|};
                }
            }
            """;

        await CreateCodeFixTest<AwaitAssertionAnalyzerType, AwaitAssertionCodeFixProviderType>(source, source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_WhenTheAssertionReturnsAValueThatIsATask()
    {
        // Assert.Single<T> returns T, so these return a Task without the assertion being asynchronous
        var source = """
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M(Dictionary<string, Task> tasksByName)
                {
                    Task[] items = [];
                    Assert.Single(items);
                    Assert.Single(items, item => item is not null);
                    Assert.Contains("a", tasksByName);
                }
            }
            """;

        await CreateAnalyzerTest<AwaitAssertionAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportsDiagnostic_OnlyForAnExpressionStatement()
    {
        // Discarding the task or assigning it observes the result, so the assertion is not lost
        var source = """
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static Task M(IAsyncEnumerable<int> actual)
                {
                    _ = Assert.Empty(actual);
                    var task = Assert.Empty(actual);

                    {|MFAS0048:Assert.Empty(actual)|};

                    return task;
                }
            }
            """;

        await CreateAnalyzerTest<AwaitAssertionAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_DoesNotReportDiagnostic_WhenTaskIsConsumed()
    {
        var source = """
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static async Task Awaited(IAsyncEnumerable<int> actual)
                {
                    await Assert.Empty(actual);
                }

                public static Task Returned(IAsyncEnumerable<int> actual)
                {
                    return Assert.Empty(actual);
                }

                public static void Synchronous(List<int> actual, int value)
                {
                    // These overloads do not return a task
                    Assert.Empty(actual);
                    Assert.Equal(42, value);
                }

                public static void TaskPassedAsAnArgument(Task<int> a, Task<int> b)
                {
                    // The rule looks at what the assertion returns, not at the code passed to it
                    Assert.Equal(a, b);
                    Assert.Equal(42, a);
                }
            }
            """;

        await CreateAnalyzerTest<AwaitAssertionAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
