using AwaitableDelegateAnalyzerType = Meziantou.Framework.Analyzers.Assertions.AwaitableDelegateAnalyzer;
using AwaitableDelegateCodeFixProviderType = Meziantou.Framework.Analyzers.Assertions.AwaitableDelegateCodeFixProvider;

namespace Meziantou.Framework.Tests;

public sealed class AwaitableDelegateRuleTests : AssertionsAnalyzerTestBase
{
    private const string Members = """
            private static event EventHandler? Changed;

            private static int IntMethod() => 0;
            private static Task TaskMethod() => Task.CompletedTask;
            private static ValueTask ValueTaskMethod() => default;
            private static ValueTask<int> ValueTaskIntMethod() => default;
            private static ValueTask<object?> ValueTaskObjectMethod() => default;
            private static CustomAwaitable CustomAwaitableMethod() => new();
        }

        public sealed class CustomAwaitable
        {
            public TaskAwaiter GetAwaiter() => Task.CompletedTask.GetAwaiter();
        }
        """;

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForValueTaskOfTInSyncMethod()
    {
        var source = $$"""
            #nullable enable
            using System;
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;
            using Meziantou.Framework.Assertions;

            public static class TestClass
            {
                public static void M()
                {
                    Assert.DoesNotThrow({|MFAS0057:() => ValueTaskIntMethod()|});
                }

            {{Members}}
            """;

        var fixedSource = $$"""
            #nullable enable
            using System;
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;
            using Meziantou.Framework.Assertions;

            public static class TestClass
            {
                public static async Task M()
                {
                    await Assert.DoesNotThrow(async () => await ValueTaskIntMethod());
                }

            {{Members}}
            """;

        await CreateCodeFixTest<AwaitableDelegateAnalyzerType, AwaitableDelegateCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForConfiguredTaskAwaitableWhoseResultIsUsed()
    {
        var source = $$"""
            #nullable enable
            using System;
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;
            using Meziantou.Framework.Assertions;

            public static class TestClass
            {
                public static async Task M()
                {
                    var exception = Assert.Throws<InvalidOperationException>({|MFAS0057:() => TaskMethod().ConfigureAwait(false)|});
                    Assert.Same(exception, Assert.ThrowsAny<Exception>({|MFAS0057:() => ValueTaskObjectMethod().ConfigureAwait(false)|}));
                    await Task.Yield();
                }

            {{Members}}
            """;

        var fixedSource = $$"""
            #nullable enable
            using System;
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;
            using Meziantou.Framework.Assertions;

            public static class TestClass
            {
                public static async Task M()
                {
                    var exception = await Assert.Throws<InvalidOperationException>(async () => await TaskMethod().ConfigureAwait(false));
                    Assert.Same(exception, await Assert.ThrowsAny<Exception>(async () => await ValueTaskObjectMethod().ConfigureAwait(false)));
                    await Task.Yield();
                }

            {{Members}}
            """;

        await CreateCodeFixTest<AwaitableDelegateAnalyzerType, AwaitableDelegateCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForMethodGroupReturningCustomAwaitable()
    {
        var source = $$"""
            #nullable enable
            using System;
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;
            using Meziantou.Framework.Assertions;

            public static class TestClass
            {
                public static void M()
                {
                    Assert.Throws(typeof(InvalidOperationException), {|MFAS0057:CustomAwaitableMethod|});
                }

            {{Members}}
            """;

        var fixedSource = $$"""
            #nullable enable
            using System;
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;
            using Meziantou.Framework.Assertions;

            public static class TestClass
            {
                public static async Task M()
                {
                    await Assert.Throws(typeof(InvalidOperationException), async () => await CustomAwaitableMethod());
                }

            {{Members}}
            """;

        await CreateCodeFixTest<AwaitableDelegateAnalyzerType, AwaitableDelegateCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_ForEventAssertion()
    {
        var source = $$"""
            #nullable enable
            using System;
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;
            using Meziantou.Framework.Assertions;

            public static class TestClass
            {
                public static async Task M()
                {
                    Assert.DoesNotRaise(handler => Changed += handler, handler => Changed -= handler, {|MFAS0057:() => ValueTaskIntMethod()|});
                    await Task.Yield();
                }

            {{Members}}
            """;

        var fixedSource = $$"""
            #nullable enable
            using System;
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;
            using Meziantou.Framework.Assertions;

            public static class TestClass
            {
                public static async Task M()
                {
                    await Assert.DoesNotRaise(handler => Changed += handler, handler => Changed -= handler, async () => await ValueTaskIntMethod());
                    await Task.Yield();
                }

            {{Members}}
            """;

        await CreateCodeFixTest<AwaitableDelegateAnalyzerType, AwaitableDelegateCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_AndCodeFix_WrapsTheAssertionInANonAsyncLambda()
    {
        var source = $$"""
            #nullable enable
            using System;
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;
            using Meziantou.Framework.Assertions;

            public static class TestClass
            {
                public static Func<int> M()
                {
                    return () =>
                    {
                        Assert.DoesNotThrow({|MFAS0057:() => ValueTaskIntMethod()|});
                        return 0;
                    };
                }

            {{Members}}
            """;

        var fixedSource = $$"""
            #nullable enable
            using System;
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;
            using Meziantou.Framework.Assertions;

            public static class TestClass
            {
                public static Func<int> M()
                {
                    return () =>
                    {
                        Assert.DoesNotThrow(async () => await ValueTaskIntMethod());
                        return 0;
                    };
                }

            {{Members}}
            """;

        await CreateCodeFixTest<AwaitableDelegateAnalyzerType, AwaitableDelegateCodeFixProviderType>(source, fixedSource).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_ReportDiagnostic_WithoutCodeFix_ForAsyncVoidLambdaBlockBodyOrActionOnlyOverload()
    {
        var source = $$"""
            #nullable enable
            using System;
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;
            using Meziantou.Framework.Assertions;

            public static class TestClass
            {
                public static void M()
                {
                    Assert.Raises<EventArgs>(handler => GenericChanged += handler, handler => GenericChanged -= handler, {|MFAS0057:async () => await Task.Yield()|});
                    Assert.Raises<EventArgs>(handler => GenericChanged += handler, handler => GenericChanged -= handler, {|MFAS0057:() => ValueTaskMethod()|});
                    Assert.DoesNotThrowAny<Exception>({|MFAS0057:() => { return CustomAwaitableMethod(); }|});
                }

                private static event EventHandler<EventArgs>? GenericChanged;

            {{Members}}
            """;

        await CreateCodeFixTest<AwaitableDelegateAnalyzerType, AwaitableDelegateCodeFixProviderType>(source, source).RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task Analyzer_NoDiagnostic_ForAwaitedOrSynchronousDelegates()
    {
        var source = $$"""
            #nullable enable
            using System;
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;
            using Meziantou.Framework.Assertions;

            public static class TestClass
            {
                public static async Task M(Action action, Func<object?> function)
                {
                    Assert.DoesNotThrow(() => IntMethod());
                    Assert.DoesNotThrow(() => { });
                    Assert.DoesNotThrow(action);
                    Assert.DoesNotThrow(function);
                    Assert.DoesNotThrow(() => _ = TaskMethod().ConfigureAwait(false));
                    Assert.DoesNotThrow(() => { _ = ValueTaskIntMethod().ConfigureAwait(false); });
                    Assert.Throws<InvalidOperationException>(() => { Func<ValueTask<int>> nested = () => ValueTaskIntMethod(); return nested; });
                    await Assert.DoesNotThrow(() => TaskMethod());
                    await Assert.DoesNotThrow(() => ValueTaskMethod());
                    await Assert.DoesNotThrow(async () => await ValueTaskIntMethod());
                    await Assert.Throws<InvalidOperationException>(async () => await ValueTaskIntMethod());
                    await Assert.Raise(handler => Changed += handler, handler => Changed -= handler, async () => await Task.Yield());
                    Assert.Equal(0, IntMethod());
                }

            {{Members}}
            """;

        await CreateAnalyzerTest<AwaitableDelegateAnalyzerType>(source).RunAsync(XunitCancellationToken);
    }
}
