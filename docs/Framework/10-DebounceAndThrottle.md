---
title: Debounce and throttle
description: Run an action once after a burst of calls, or at most once per interval, with the arguments of the last call.
---

Events such as key presses, file system notifications or window resizes can arrive in bursts. Debounce and throttle
wrap an `Action` so that a burst runs it once.

## Debounce

A debounced action runs once the calls stop: when the interval has elapsed since the last call.

<!-- ref:../../tests/Meziantou.Framework.Tests/DebounceExtensionsTests.cs;region=DebounceExtensions_Debounce;format=md-fenced-code-block;dedent=true -->
```csharp
var timeProvider = new FakeTimeProvider();
var searches = new List<string>();
var search = DebounceExtensions.Debounce<string>(text => searches.Add(text), TimeSpan.FromMilliseconds(300), timeProvider);

// The user types quickly: each call restarts the countdown
search("m");
timeProvider.Advance(TimeSpan.FromMilliseconds(100));
search("me");
timeProvider.Advance(TimeSpan.FromMilliseconds(100));
search("mez");
Assert.Empty(searches);

// 300 ms after the last call, the action runs once, with the last arguments
timeProvider.Advance(TimeSpan.FromMilliseconds(300));
Assert.Equal(["mez"], searches);
```
<!-- endref -->

Each call restarts the countdown, so the action does not run while the calls continue. Use it for a search box, to
save a document after the user stops typing, or to react once to a burst of file changes.

## Throttle

A throttled action runs at most once per interval, even when the calls never stop:

<!-- ref:../../tests/Meziantou.Framework.Tests/ThrottleExtensionsTests.cs;region=ThrottleExtensions_Throttle;format=md-fenced-code-block;dedent=true -->
```csharp
var timeProvider = new FakeTimeProvider();
var saved = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
var save = ThrottleExtensions.Throttle<int>(version => saved.TrySetResult(version), TimeSpan.FromSeconds(1), timeProvider);

// The first call schedules the action; the next calls only update the arguments
save(1);
save(2);
save(3);

// The action runs once the interval has elapsed, with the last arguments
timeProvider.Advance(TimeSpan.FromSeconds(1));
Assert.Equal(3, await saved.Task);
```
<!-- endref -->

The first call schedules the action after the interval. The calls made before it runs only update the arguments. Once
it has run, the next call schedules it again. Use it to refresh a progress display or to report changes regularly.

## Common behavior

- `Debounce` and `Throttle` are extension methods on `Action` and `Action<T0, ...>` with up to 9 parameters for
  `Debounce` and 10 for `Throttle`. The action receives the arguments of the last call.
- With `TimeProvider.System`, the action runs on a thread-pool thread, not on the thread that called the delegate. In a
  UI application, dispatch to the UI thread inside the action.
- The overloads that take a `TimeProvider` let tests control time, with `FakeTimeProvider` from the
  `Microsoft.Extensions.TimeProvider.Testing` package, as in the examples. The others use `TimeProvider.System`.
- Keep a reference to the returned delegate and call it each time: calling `Debounce` or `Throttle` again creates an
  independent delegate.
