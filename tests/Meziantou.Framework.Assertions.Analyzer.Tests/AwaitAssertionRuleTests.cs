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
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForTaskArgument()
    {
        var source = """
            using System.Threading.Tasks;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static void M()
                {
                    Assert.Equal(42, {|MFAS0049:GetValueAsync()|});
                }

                private static Task<int> GetValueAsync() => Task.FromResult(42);
            }
            """;

        var fixedSource = """
            using System.Threading.Tasks;
            using Meziantou.Framework.Assertions;

            namespace Sample;

            public static class TestClass
            {
                public static async Task M()
                {
                    Assert.Equal(42, await GetValueAsync());
                }

                private static Task<int> GetValueAsync() => Task.FromResult(42);
            }
            """;

        await CreateCodeFixTest<AwaitAssertionAnalyzerType, AwaitAssertionCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
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

                public static void ComparingTwoTasks(Task<int> a, Task<int> b)
                {
                    Assert.Equal(a, b);
                }
            }
            """;

        await CreateAnalyzerTest<AwaitAssertionAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
