---
title: Observability
description: Trace and measure the parsing, the compilation and the execution of the scripts with OpenTelemetry.
---

## Collect the traces and the metrics

The engine reports what it does with the diagnostics of .NET: an `ActivitySource` for the traces and a `Meter` for the
metrics, both named `Meziantou.Framework.JavaScriptEngine`
([JsEngineTelemetry.ActivitySourceName](cref:Meziantou.Framework.JavaScriptEngine.JsEngineTelemetry.ActivitySourceName) and
[JsEngineTelemetry.MeterName](cref:Meziantou.Framework.JavaScriptEngine.JsEngineTelemetry.MeterName)). With OpenTelemetry,
add them with `AddSource` and `AddMeter`. Without OpenTelemetry, an `ActivityListener` and a `MeterListener` receive them:

<!-- ref:../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_Telemetry;format=md-fenced-code-block;dedent=true -->
```csharp
// With OpenTelemetry: .AddSource(JsEngineTelemetry.ActivitySourceName) and .AddMeter(JsEngineTelemetry.MeterName)
var spans = new ConcurrentQueue<Activity>();
using var activityListener = new ActivityListener
{
    ShouldListenTo = source => source.Name == JsEngineTelemetry.ActivitySourceName,
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = spans.Enqueue,
};
ActivitySource.AddActivityListener(activityListener);

var measurements = new ConcurrentQueue<(string Instrument, object? Kind)>();
using var meterListener = new MeterListener
{
    InstrumentPublished = (instrument, listener) =>
    {
        if (instrument.Meter.Name == JsEngineTelemetry.MeterName)
        {
            listener.EnableMeasurementEvents(instrument);
        }
    },
};
meterListener.SetMeasurementEventCallback<double>((instrument, _, tags, _) =>
{
    foreach (var tag in tags)
    {
        if (tag.Key == "js.code.kind")
        {
            measurements.Enqueue((instrument.Name, tag.Value));
        }
    }
});
meterListener.Start();

using var engine = new JsEngine();
var realm = engine.CreateRealm();
realm.Execute("function add(a, b) { return a + b; } add(1, 2);", "telemetry-sample.js");

// A span for the script, with the parsing and the compilation as children
var execute = Assert.Single(spans, span => span.OperationName == "js.script.execute" && (string?)span.GetTagItem("js.source.url") == "telemetry-sample.js");
var children = spans.Where(span => span.ParentSpanId == execute.SpanId).Select(span => span.OperationName).Order(StringComparer.Ordinal);
Assert.Equal(["js.compile", "js.parse"], children);

// The metrics measure the parsing, the compilation and the execution, including the functions compiled on their first call
Assert.Contains(("meziantou.framework.javascript_engine.parse.duration", "script"), measurements);
Assert.Contains(("meziantou.framework.javascript_engine.compile.duration", "lazy_function"), measurements);
Assert.Contains(("meziantou.framework.javascript_engine.execution.duration", "script"), measurements);
```
<!-- endref -->

Nothing is recorded when nothing listens: the engine does not slow down the scripts of an application that does not
collect them.

## Traces

The engine creates a span for each operation that the host starts. The spans are children of the current activity, so
the work of a script appears in the trace of the request that runs it.

| Span | Started by | Children |
| --- | --- | --- |
| `js.script.execute` | [JsRealm.Execute](cref:Meziantou.Framework.JavaScriptEngine.JsRealm.Execute) | `js.parse` and `js.compile`, when the source text is compiled by the call (not for a prepared script) |
| `js.parse` | the parsing of a script or a module: [JsRealm.Execute](cref:Meziantou.Framework.JavaScriptEngine.JsRealm.Execute), [JsPreparedScript.Parse](cref:Meziantou.Framework.JavaScriptEngine.JsPreparedScript.Parse), [JsRealm.ParseModule](cref:Meziantou.Framework.JavaScriptEngine.JsRealm.ParseModule) | |
| `js.compile` | the compilation of a script or a module to bytecode, after `js.parse` | |
| `js.module.link` | [JsModule.Link](cref:Meziantou.Framework.JavaScriptEngine.JsModule.Link) | |
| `js.module.evaluate` | [JsModule.Evaluate](cref:Meziantou.Framework.JavaScriptEngine.JsModule.Evaluate) | |

The spans have these tags:

| Tag | Value |
| --- | --- |
| `js.code.kind` | `script` or `module` |
| `js.source.url` | The URL of the script or the module, when it has one |
| `js.source.length` | The length of the source text, in UTF-16 code units (not on the spans of the modules that are linked or evaluated) |
| `error.type` | The full name of the exception type, when the operation fails |
| `js.termination.reason` | `canceled`, `timeout`, `memory_limit` or `statement_limit`, when a limit or a cancellation stops the script (see [Limits and sandboxing](07-LimitsAndSandboxing.md)) |

A failed operation has the `Error` status, and an `exception` event. A syntax error fails `js.parse` with
[JsSyntaxErrorException](cref:Meziantou.Framework.JavaScriptEngine.JsSyntaxErrorException), and the
`js.script.execute` span with [JsException](cref:Meziantou.Framework.JavaScriptEngine.JsException) (the SyntaxError the
script receives).

`js.module.evaluate` covers the evaluation that runs during the call: the modules of the graph without top-level `await`.
The evaluation of a module that awaits continues in the jobs ([RunJobs](cref:Meziantou.Framework.JavaScriptEngine.JsEngine.RunJobs)),
after the span ends. A module whose evaluation throws rejects the returned promise instead of throwing: the span has the
`Error` status when the error is known at the end of the call.

The code compiled while a script runs (`eval`, the `Function` constructor, a function compiled on its first call, a hot
function compiled to .NET code) has no span: a script can do it thousands of times, which would flood the traces. The
metrics measure it.

## Metrics

| Metric | Type | Unit | Tags |
| --- | --- | --- | --- |
| `meziantou.framework.javascript_engine.parse.duration` | Histogram | `s` | `js.code.kind`, `error.type` |
| `meziantou.framework.javascript_engine.compile.duration` | Histogram | `s` | `js.code.kind`, `error.type` |
| `meziantou.framework.javascript_engine.execution.duration` | Histogram | `s` | `js.code.kind`, `error.type` |
| `meziantou.framework.javascript_engine.terminations` | Counter | `{termination}` | `js.termination.reason` |

`js.code.kind` tells what code was parsed, compiled or run:

| Value | Code |
| --- | --- |
| `script` | A script run by [JsRealm.Execute](cref:Meziantou.Framework.JavaScriptEngine.JsRealm.Execute) or compiled by [JsPreparedScript.Parse](cref:Meziantou.Framework.JavaScriptEngine.JsPreparedScript.Parse) |
| `module` | A module parsed by [JsRealm.ParseModule](cref:Meziantou.Framework.JavaScriptEngine.JsRealm.ParseModule) or evaluated by [JsModule.Evaluate](cref:Meziantou.Framework.JavaScriptEngine.JsModule.Evaluate) |
| `eval` | The code of `eval` and of `ShadowRealm.prototype.evaluate` |
| `function` | A function created by the `Function` constructor (and the async and generator variants) |
| `lazy_function` | A function compiled on its first call: the engine checks the syntax of the whole source text, but compiles the body of a function when it is called |
| `jit` | A hot function compiled from bytecode to .NET code (`compile.duration` only; not with Native AOT) |

The three durations do not overlap: `execution.duration` measures the run of a script without its parsing and its
compilation. It includes the functions the script compiles while it runs, which `parse.duration` and
`compile.duration` also measure with the `lazy_function`, `eval`, `function` and `jit` kinds. The execution of a module
includes the evaluation of the modules it imports. The metrics have no URL, to keep the number of time series small:
use the traces to find the slow scripts.

`terminations` counts the entries into JavaScript stopped by a limit of [JsEngineOptions](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions)
or of an execution scope, or by a cancellation.
