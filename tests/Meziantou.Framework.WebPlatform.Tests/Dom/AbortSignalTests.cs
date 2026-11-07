using System.Runtime.CompilerServices;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>https://dom.spec.whatwg.org/#abort-signal-garbage-collection: source signals and dependent signals are weak sets.</summary>
public sealed class AbortSignalTests
{
    [Fact]
    public void Any_DependentSignalDoesNotKeepItsSourceSignalAlive()
    {
        var (dependentSignal, sourceSignal) = CreateDependentSignalOfCollectableSource();
        CollectGarbage();

        Assert.False(sourceSignal.TryGetTarget(out _));
        Assert.False(dependentSignal.Aborted);
        GC.KeepAlive(dependentSignal);
    }

    [Fact]
    public void Any_SourceSignalDoesNotKeepADependentSignalWithoutListenersAlive()
    {
        var controller = new AbortController();
        var dependentSignal = CreateDependentSignal(controller.Signal, onAbort: null);
        CollectGarbage();

        Assert.False(dependentSignal.TryGetTarget(out _));
        controller.Abort();
    }

    [Fact]
    public void Any_SourceSignalKeepsADependentSignalWithAbortListenersAlive()
    {
        var log = new List<string>();
        var controller = new AbortController();
        var dependentSignal = CreateDependentSignal(controller.Signal, _ => log.Add("abort"));
        CollectGarbage();

        Assert.True(dependentSignal.TryGetTarget(out _));
        controller.Abort("reason");
        Assert.Equal(["abort"], log);
    }

    [Fact]
    public void Any_SourceSignalKeepsADependentSignalWithAbortAlgorithmsAlive()
    {
        var log = new List<string>();
        var controller = new AbortController();
        var dependentSignal = CreateDependentSignalWithAbortAlgorithm(controller.Signal, () => log.Add("algorithm"));
        CollectGarbage();

        Assert.True(dependentSignal.TryGetTarget(out _));
        controller.Abort();
        Assert.Equal(["algorithm"], log);
    }

    [Fact]
    public void Any_DependentSignalIsCollectableOnceItsAbortListenerIsRemoved()
    {
        var controller = new AbortController();
        var dependentSignal = CreateDependentSignalWithRemovedListener(controller.Signal);
        CollectGarbage();

        Assert.False(dependentSignal.TryGetTarget(out _));
        GC.KeepAlive(controller);
    }

    [Fact]
    public void Any_AbortedDependentSignalIsReleasedByItsOtherSourceSignals()
    {
        var controller1 = new AbortController();
        var controller2 = new AbortController();
        var dependentSignal = CreateDependentSignal([controller1.Signal, controller2.Signal], _ => { });
        controller1.Abort();
        CollectGarbage();

        Assert.False(dependentSignal.TryGetTarget(out _));
        GC.KeepAlive(controller2);
    }

    [Fact]
    public void Any_DependentOfADependentSignalUsesTheLiveSourceSignals()
    {
        var log = new List<string>();
        var controller = new AbortController();
        var dependentSignal = AbortSignal.Any([controller.Signal]);
        var signal = AbortSignal.Any([dependentSignal]);
        signal.AddEventListener("abort", _ => log.Add("abort"));

        controller.Abort("reason");
        Assert.True(dependentSignal.Aborted);
        Assert.Equal("reason", signal.Reason);
        Assert.Equal(["abort"], log);
    }

    [Fact]
    public void Any_ManyCollectedDependentSignalsStillAbortTheLiveOnes()
    {
        var log = new List<string>();
        var controller = new AbortController();
        for (var i = 0; i < 1000; i++)
        {
            _ = AbortSignal.Any([controller.Signal]);
        }

        var signal = AbortSignal.Any([controller.Signal]);
        signal.AddEventListener("abort", _ => log.Add("abort"));
        CollectGarbage();
        for (var i = 0; i < 1000; i++)
        {
            _ = AbortSignal.Any([controller.Signal]);
        }

        controller.Abort();
        Assert.Equal(["abort"], log);
    }

    private static void CollectGarbage()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (AbortSignal DependentSignal, WeakReference<AbortSignal> SourceSignal) CreateDependentSignalOfCollectableSource()
    {
        var controller = new AbortController();
        return (AbortSignal.Any([controller.Signal]), new WeakReference<AbortSignal>(controller.Signal));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<AbortSignal> CreateDependentSignal(AbortSignal sourceSignal, Action<Event>? onAbort) => CreateDependentSignal([sourceSignal], onAbort);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<AbortSignal> CreateDependentSignal(AbortSignal[] sourceSignals, Action<Event>? onAbort)
    {
        var signal = AbortSignal.Any(sourceSignals);
        if (onAbort is not null)
        {
            signal.AddEventListener("abort", onAbort);
        }

        return new WeakReference<AbortSignal>(signal);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<AbortSignal> CreateDependentSignalWithAbortAlgorithm(AbortSignal sourceSignal, Action algorithm)
    {
        var signal = AbortSignal.Any([sourceSignal]);
        signal.AddAbortAlgorithm(algorithm);
        return new WeakReference<AbortSignal>(signal);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<AbortSignal> CreateDependentSignalWithRemovedListener(AbortSignal sourceSignal)
    {
        var signal = AbortSignal.Any([sourceSignal]);
        Action<Event> listener = _ => { };
        signal.AddEventListener("abort", listener);
        signal.RemoveEventListener("abort", listener);
        return new WeakReference<AbortSignal>(signal);
    }
}
