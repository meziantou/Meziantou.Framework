---
title: Telemetry
description: Trace process executions with System.Diagnostics activities.
---

Each execution creates an `Activity` from the `Meziantou.Framework.ProcessWrapper` activity source. Listen to this source
with OpenTelemetry or an `ActivityListener` to trace the processes your application starts.

<!-- ref:../../tests/Meziantou.Framework.ProcessWrapper.Tests/ProcessWrapperTests.cs;region=ProcessWrapper_Telemetry;format=md-fenced-code-block;dedent=true -->
```csharp
// Collect the activities of the current test only (ScopedActivityListener is in Meziantou.Framework)
using var listener = new ScopedActivityListener(new ScopedActivityListenerOptions
{
    SourceNames = ["Meziantou.Framework.ProcessWrapper"],
    SamplingResult = ActivitySamplingResult.AllDataAndRecorded,
});

await ProcessWrapper.Create("dotnet")
    .WithArguments("--version")
    .ExecuteAsync();

var activity = Assert.Single(listener.Activities);
Assert.Equal("process.execute", activity.OperationName);
Assert.Equal(0, activity.GetTagItem("process.exit.code"));
Assert.IsType<string>(activity.GetTagItem("process.executable.path"));
```
<!-- endref -->

| | Value |
| --- | --- |
| Source name | `Meziantou.Framework.ProcessWrapper` |
| Operation name | `process.execute` |
| `process.executable.path` tag | The resolved path of the executable |
| `process.exit.code` tag | The exit code, an `int`, once the process exited |
| Status | `Error` when the [validation](06-ExitCodesAndErrors.md) fails, with the error message as description |

The activity starts when the process starts and stops when it exits. Its status is not set when the execution is
canceled. The activity is not the ambient activity of your code: the code that runs after `ExecuteAsync` keeps its own
`Activity.Current`.

The error description is the message of the exception, so it shows the command as allowed by the
[log verbosity](06-ExitCodesAndErrors.md#what-messages-show). The arguments are never added as a tag.
