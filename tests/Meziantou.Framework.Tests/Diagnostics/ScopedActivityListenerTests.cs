using System.Diagnostics;
using System.Globalization;
using Meziantou.Framework.Diagnostics;

namespace Meziantou.Framework.Tests.Diagnostics;

public sealed class ScopedActivityListenerTests
{
    // Every test uses its own ActivitySource, and the listener is restricted to it, so the tests of this class can run in parallel
    private static ActivitySource CreateSource() => new("test-" + Guid.NewGuid().ToString("N"));

    private static ScopedActivityListener CreateListener(ActivitySource source, Action<ScopedActivityListenerOptions>? configure = null)
    {
        var options = new ScopedActivityListenerOptions { SourceNames = [source.Name] };
        configure?.Invoke(options);
        return new ScopedActivityListener(options);
    }

    // Keeps the source sampled outside of any scope, so activities can be created before or after the scope
    private static ActivityListener CreateAlwaysOnListener(ActivitySource source)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == source.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };

        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static string[] OperationNames(IEnumerable<Activity> activities) => [.. activities.Select(activity => activity.OperationName)];

    [Fact]
    public void ActivityStartedInsideScope_IsCaptured()
    {
        using var source = CreateSource();
        using var listener = CreateListener(source);

        using (source.StartActivity("op"))
        {
        }

        Assert.Equal(["op"], OperationNames(listener.Activities));
    }

    [Fact]
    public void ActivityStartedBeforeScope_ButStoppedInsideScope_IsNotCaptured()
    {
        using var source = CreateSource();
        using var alwaysOn = CreateAlwaysOnListener(source);

        var activity = source.StartActivity("op");
        Assert.NotNull(activity);

        using var listener = CreateListener(source);
        activity.Stop();

        Assert.Empty(listener.Activities);
    }

    [Fact]
    public void ActivityStartedAfterDispose_IsNotCaptured()
    {
        using var source = CreateSource();
        using var alwaysOn = CreateAlwaysOnListener(source);
        var listener = CreateListener(source);
        listener.Dispose();

        using (source.StartActivity("op"))
        {
        }

        Assert.Empty(listener.Activities);
    }

    [Fact]
    public void ActivityStillRunningWhenDisposed_IsNotCaptured()
    {
        using var source = CreateSource();
        var listener = CreateListener(source);

        var activity = source.StartActivity("op");
        Assert.NotNull(activity);
        listener.Dispose();
        activity.Stop();

        Assert.Empty(listener.Activities);
    }

    [Fact]
    public async Task ActivityOnFlowStartedBeforeScope_IsNotCaptured()
    {
        using var source = CreateSource();
        using var alwaysOn = CreateAlwaysOnListener(source);

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = Task.Run(async () =>
        {
            await gate.Task;
            using (source.StartActivity("op"))
            {
            }
        }, TestContext.Current.CancellationToken);

        using var listener = CreateListener(source, options => options.CaptureChildActivities = false);
        gate.SetResult();
        await task;

        Assert.Empty(listener.Activities);
    }

    [Fact]
    public async Task ActivityOnTaskStartedInsideScope_IsCaptured()
    {
        using var source = CreateSource();
        using var listener = CreateListener(source);

        await Task.Run(() =>
        {
            using (source.StartActivity("op"))
            {
            }
        }, TestContext.Current.CancellationToken);

        Assert.Equal(["op"], OperationNames(listener.Activities));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChildActivityWithLostAsyncScope_IsCapturedOnlyWhenCaptureChildActivitiesIsEnabled(bool captureChildActivities)
    {
        using var source = CreateSource();
        using var listener = CreateListener(source, options => options.CaptureChildActivities = captureChildActivities);

        var parent = source.StartActivity("parent");
        Assert.NotNull(parent);

        Task<Activity?> task;
        using (ExecutionContext.SuppressFlow())
        {
            task = Task.Run(() =>
            {
                var child = source.StartActivity("child", ActivityKind.Internal, parent.Context);
                child?.Stop();
                return child;
            }, TestContext.Current.CancellationToken);
        }

        await task;
        parent.Stop();

        var expected = captureChildActivities ? new[] { "child", "parent" } : ["parent"];
        Assert.Equal(expected, OperationNames(listener.Activities));
    }

    [Fact]
    public void NestedScopes_BothCaptureTheActivity()
    {
        using var source = CreateSource();
        using var outer = CreateListener(source);
        using var inner = CreateListener(source);

        using (source.StartActivity("op"))
        {
        }

        Assert.Equal(["op"], OperationNames(outer.Activities));
        Assert.Equal(["op"], OperationNames(inner.Activities));
    }

    [Fact]
    public void NestedScopes_WhenInnerIsDisposed_OuterKeepsCapturing()
    {
        using var source = CreateSource();
        using var outer = CreateListener(source);
        var inner = CreateListener(source);
        inner.Dispose();

        using (source.StartActivity("op"))
        {
        }

        Assert.Equal(["op"], OperationNames(outer.Activities));
        Assert.Empty(inner.Activities);
    }

    [Fact]
    public async Task ParallelScopes_DoNotSeeEachOther()
    {
        using var source = CreateSource();

        var task1 = Task.Run(() =>
        {
            using var listener = CreateListener(source);
            using (source.StartActivity("a"))
            {
            }

            return OperationNames(listener.Activities);
        }, TestContext.Current.CancellationToken);

        var task2 = Task.Run(() =>
        {
            using var listener = CreateListener(source);
            using (source.StartActivity("b"))
            {
            }

            return OperationNames(listener.Activities);
        }, TestContext.Current.CancellationToken);

        Assert.Equal(["a"], await task1);
        Assert.Equal(["b"], await task2);
    }

    // The activities created with "new Activity(name)" belong to the process-wide source with an empty name
    [Fact(DisableParallelization = true)]
    public void LegacyActivity_IsCaptured()
    {
        using var listener = new ScopedActivityListener(new ScopedActivityListenerOptions { SourceNames = [""] });

        using var activity = new Activity("legacy");
        activity.Start();
        activity.Stop();

        Assert.Contains(listener.Activities, item => item.OperationName == "legacy");
    }

    [Fact]
    public void SourceNames_FiltersOtherSources()
    {
        using var source1 = CreateSource();
        using var source2 = CreateSource();
        using var alwaysOn = CreateAlwaysOnListener(source2);
        using var listener = CreateListener(source1);

        using (source1.StartActivity("a"))
        {
        }

        using (source2.StartActivity("b"))
        {
        }

        Assert.Equal(["a"], OperationNames(listener.Activities));
    }

    [Fact]
    public async Task OutOfScope_DoesNotForceSamplingForOtherListeners()
    {
        using var source = CreateSource();

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = Task.Run(async () =>
        {
            await gate.Task;
            return source.StartActivity("op");
        }, TestContext.Current.CancellationToken);

        using var listener = CreateListener(source);
        gate.SetResult();

        Assert.Null(await task);
    }

    [Theory]
    [InlineData(ActivitySamplingResult.AllData, false)]
    [InlineData(ActivitySamplingResult.AllDataAndRecorded, true)]
    public void SamplingResult_ControlsTheRecordedFlag(ActivitySamplingResult samplingResult, bool expectedRecorded)
    {
        using var source = CreateSource();
        using var listener = CreateListener(source, options => options.SamplingResult = samplingResult);

        using var activity = source.StartActivity("op");

        Assert.NotNull(activity);
        Assert.Equal(expectedRecorded, activity.Recorded);
    }

    [Fact]
    public void ActivityStartedAndActivityStoppedEvents_AreRaised()
    {
        using var source = CreateSource();
        using var listener = CreateListener(source);

        var events = new List<string>();
        listener.ActivityStarted += (sender, e) => events.Add("start:" + e.Activity.OperationName);
        listener.ActivityStopped += (sender, e) => events.Add("stop:" + e.Activity.OperationName);

        using (source.StartActivity("op"))
        {
        }

        Assert.Equal(["start:op", "stop:op"], events);
    }

    [Fact]
    public void ThrowingEventHandler_DoesNotBreakTheObservedCode()
    {
        using var source = CreateSource();
        using var listener = CreateListener(source);

        listener.ActivityStarted += (sender, e) => throw new InvalidOperationException("Started");
        listener.ActivityStopped += (sender, e) => throw new InvalidOperationException("Stopped");

        using (source.StartActivity("op"))
        {
        }

        Assert.Equal(["op"], OperationNames(listener.Activities));
    }

    [Fact]
    public async Task GetActivitiesAsync_ReplaysAlreadyCapturedActivities()
    {
        using var source = CreateSource();
        using var listener = CreateListener(source);

        using (source.StartActivity("a"))
        {
        }

        using (source.StartActivity("b"))
        {
        }

        listener.Dispose();

        var result = new List<string>();
        await foreach (var activity in listener.GetActivitiesAsync(TestContext.Current.CancellationToken))
        {
            result.Add(activity.OperationName);
        }

        Assert.Equal(["a", "b"], result);
    }

    [Fact]
    public async Task GetActivitiesAsync_StreamsActivitiesAndCompletesOnDispose()
    {
        using var source = CreateSource();
        using var listener = CreateListener(source);

        var consumer = Task.Run(async () =>
        {
            var result = new List<string>();
            await foreach (var activity in listener.GetActivitiesAsync(TestContext.Current.CancellationToken))
            {
                result.Add(activity.OperationName);
            }

            return result;
        }, TestContext.Current.CancellationToken);

        using (source.StartActivity("a"))
        {
        }

        listener.Dispose();

        Assert.Equal(["a"], await consumer);
    }

    [Fact]
    public async Task GetActivitiesAsync_SupportsMultipleConsumers()
    {
        using var source = CreateSource();
        using var listener = CreateListener(source);

        var consumer1 = Task.Run(async () =>
        {
            var result = new List<string>();
            await foreach (var activity in listener.GetActivitiesAsync(TestContext.Current.CancellationToken))
            {
                result.Add(activity.OperationName);
            }

            return result;
        }, TestContext.Current.CancellationToken);

        var consumer2 = Task.Run(async () =>
        {
            var result = new List<string>();
            await foreach (var activity in listener.GetActivitiesAsync(TestContext.Current.CancellationToken))
            {
                result.Add(activity.OperationName);
            }

            return result;
        }, TestContext.Current.CancellationToken);

        using (source.StartActivity("a"))
        {
        }

        listener.Dispose();

        Assert.Equal(["a"], await consumer1);
        Assert.Equal(["a"], await consumer2);
    }

    [Fact]
    public async Task GetActivitiesAsync_HonorsCancellation()
    {
        using var source = CreateSource();
        using var listener = CreateListener(source);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var activity in listener.GetActivitiesAsync(cts.Token))
            {
            }
        });
    }

    [Fact]
    public async Task GetActivitiesAsync_DoesNotLoseOrDuplicateActivities()
    {
        const int ActivityCount = 200;

        using var source = CreateSource();
        using var listener = CreateListener(source);

        var consumer = Task.Run(async () =>
        {
            var result = new List<string>();
            await foreach (var activity in listener.GetActivitiesAsync(TestContext.Current.CancellationToken))
            {
                result.Add(activity.OperationName);
            }

            return result;
        }, TestContext.Current.CancellationToken);

        await Task.WhenAll(Enumerable.Range(0, ActivityCount).Select(i => Task.Run(() =>
        {
            using (source.StartActivity("op" + i.ToString(CultureInfo.InvariantCulture)))
            {
            }
        }, TestContext.Current.CancellationToken)));

        listener.Dispose();

        var result = await consumer;
        Assert.HasCount(ActivityCount, result);
        Assert.Distinct(result);
    }

    [Fact]
    public void MaxActivityCount_KeepsTheMostRecentActivities()
    {
        using var source = CreateSource();
        using var listener = CreateListener(source, options => options.MaxActivityCount = 2);

        for (var i = 0; i < 5; i++)
        {
            using (source.StartActivity("op" + i.ToString(CultureInfo.InvariantCulture)))
            {
            }
        }

        Assert.Equal(["op3", "op4"], OperationNames(listener.Activities));
    }

    [Fact]
    public void MaxActivityCount_MustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScopedActivityListener(new ScopedActivityListenerOptions { MaxActivityCount = 0 }));
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        using var source = CreateSource();
        using var alwaysOn = CreateAlwaysOnListener(source);
        var listener = CreateListener(source);

        listener.Dispose();
        listener.Dispose();

        using (source.StartActivity("op"))
        {
        }

        Assert.Empty(listener.Activities);
    }
}
