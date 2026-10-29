---
title: Receiving notifications
description: Handle the arguments that the next instances send to the first one, on the right thread, and handle a first instance that does not answer.
---

## Send and receive the arguments

The first instance handles the `NewInstance` event. The next instances call `NotifyFirstInstance` with their
command-line arguments:

<!-- ref:../../tests/Meziantou.Framework.SingleInstance.Tests/SingleInstanceTests.cs;region=SingleInstance_ReceiveNotification;format=md-fenced-code-block;dedent=true -->
```csharp
var applicationId = Guid.NewGuid();

using var firstInstance = new SingleInstance(applicationId);
var notification = new TaskCompletionSource<SingleInstanceEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
firstInstance.NewInstance += (sender, e) => notification.TrySetResult(e);
Assert.True(firstInstance.StartApplication());

// Usually done by another process started with the same application id
using var secondInstance = new SingleInstance(applicationId);
Assert.True(secondInstance.NotifyFirstInstance(["open", "report.txt"]));

var received = await notification.Task;
Assert.Equal(["open", "report.txt"], received.Arguments);
Assert.Equal(Environment.ProcessId, received.ProcessId);
```
<!-- endref -->

The `SingleInstanceEventArgs` of the event gives:

- `Arguments`: the arguments that the other instance passed to `NotifyFirstInstance`. They are sent as they are: pass
  the `args` of `Main`, or any other strings, such as full file paths when the arguments are paths relative to the
  current directory of the other process.
- `ProcessId`: the id of the process that sent the notification.

The `sender` of the event is the `SingleInstance` object of the first instance.

## Update the user interface

`NewInstance` is raised on a thread-pool thread, not on the thread that called `StartApplication`. A desktop
application that updates its windows from the handler must move to the UI thread first. Capture the
`SynchronizationContext` of the UI thread and post the work to it:

<!-- ref:../../samples/SingleInstanceConsoleAppSample/SingleInstanceExamples.cs;region=SingleInstance_UiThread;format=md-fenced-code-block;dedent=true -->
```csharp
// Run this code on the UI thread (WPF, Windows Forms, ...), for instance when the application starts
var uiContext = SynchronizationContext.Current;

using var singleInstance = new SingleInstance(new Guid("7b0c3c5e-4f7a-4d8e-9a43-0f3f6f1d2c11"));
singleInstance.NewInstance += (sender, e) =>
{
    // NewInstance is raised on a thread-pool thread: post the work to the UI thread
    uiContext?.Post(_ => OpenFiles(e.Arguments), state: null);
};

void OpenFiles(string[] files)
{
    // Update the UI: open the files, bring the main window to the front, ...
    foreach (var file in files)
    {
        Console.WriteLine("Opening " + file);
    }
}
```
<!-- endref -->

WPF and Windows Forms install their `SynchronizationContext` on the UI thread, so `SynchronizationContext.Current`
returns it when this code runs on that thread, for instance in the startup code of the application or of its main
window.

Keep these rules in mind when you write the handler:

- When several instances start at the same time, the handler can run on several threads at once, and the
  notifications can arrive in any order. Protect the state that the handler shares with the rest of the application.
- Catch the exceptions in the handler. An exception that escapes it is raised on a thread-pool thread, which terminates
  the process.

## When the first instance does not answer

`NotifyFirstInstance` returns `true` when the message was sent. It returns `false`, and never throws, when it cannot
reach the first instance: no instance is listening, the first instance does not accept the connection in time, or it
stops listening while the message is being sent.

<!-- ref:../../tests/Meziantou.Framework.SingleInstance.Tests/SingleInstanceTests.cs;region=SingleInstance_NotifyFailure;format=md-fenced-code-block;dedent=true -->
```csharp
using var singleInstance = new SingleInstance(Guid.NewGuid())
{
    // Wait at most 1 second for the first instance to accept the connection (the default is 3 seconds)
    ClientConnectionTimeout = TimeSpan.FromSeconds(1),
};

// No instance is listening: NotifyFirstInstance returns false instead of throwing
Assert.False(singleInstance.NotifyFirstInstance(["open", "report.txt"]));
```
<!-- endref -->

- `ClientConnectionTimeout` is the time `NotifyFirstInstance` waits for the first instance to accept the connection.
  The default is 3 seconds. `Timeout.InfiniteTimeSpan` waits forever.
- `false` happens, for instance, when the first instance was started with `StartServer = false`, or when it is
  exiting. Decide what the application does then: show a message, or exit silently.
- `NotifyFirstInstance` does not need `StartApplication` to be called first, and does not tell whether the first
  instance handled the arguments: it returns once the message is sent.
