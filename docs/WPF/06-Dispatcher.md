---
title: Dispatcher
description: Come back to the UI thread after background work with await dispatcher.SwitchToDispatcherThread().
---

WPF controls can only be used from the thread of their dispatcher, the UI thread. After running work on a thread-pool
thread, `await dispatcher.SwitchToDispatcherThread()` continues the method on the thread of the dispatcher, without
wrapping the rest of the method in a delegate:

<!-- ref:../../tests/Meziantou.Framework.WPF.Tests/DispatcherExtensionsTests.cs;region=DispatcherExtensions_SwitchToDispatcherThread;format=md-fenced-code-block;dedent=true -->
```csharp
// Do the slow work on a thread-pool thread
var lines = await Task.Run(() => Enumerable.Range(1, 1000).Select(i => $"Line {i}").ToList());

// Continue on the thread of the dispatcher, usually Application.Current.Dispatcher, to update the controls
await dispatcher.SwitchToDispatcherThread();
Assert.True(dispatcher.CheckAccess());
```
<!-- endref -->

- When the code already runs on the thread of the dispatcher, the `await` continues immediately.
- Otherwise, the rest of the method is queued to the dispatcher with `BeginInvoke`, at the default priority.
- When the dispatcher shuts down before running the continuation, for instance because the application is closing, the
  method still continues instead of waiting forever, but the controls can no longer be used. Check
  `dispatcher.HasShutdownStarted` before using them if the application can close while the work runs.
- [SwitchToDispatcherThread](cref:Meziantou.Framework.WPF.DispatcherExtensions.SwitchToDispatcherThread) is an extension method of `Dispatcher`: use `Application.Current.Dispatcher`, or the
  `Dispatcher` property of a control.
