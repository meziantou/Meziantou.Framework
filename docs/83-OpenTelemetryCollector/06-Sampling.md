---
title: Sampling
description: Drop export requests before they reach the handlers with request samplers, and choose the order in which they run.
---

A sampler decides, for each export request, whether the handlers receive it. Use one to drop the telemetry you do not
want to store, such as the requests of a noisy service or empty requests.

## Write a sampler

A sampler derives from `OpenTelemetrySampler`, and overrides `ShouldSampleLogsAsync`, `ShouldSampleTracesAsync` or
`ShouldSampleMetricsAsync`. The methods you do not override keep every request.

<!-- ref:../../tests/Meziantou.Framework.OpenTelemetry.Tests/OpenTelemetryReceiverTests.cs;region=OpenTelemetryCollector_KeepOnlyNonEmptyRequestsSampler;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class KeepOnlyNonEmptyRequestsSampler : OpenTelemetrySampler
{
    public override ValueTask<bool> ShouldSampleLogsAsync(OpenTelemetryHandlerContext context, ExportLogsServiceRequest request, CancellationToken cancellationToken)
        => ValueTask.FromResult(request.ResourceLogs.Count > 0);

    public override ValueTask<bool> ShouldSampleTracesAsync(OpenTelemetryHandlerContext context, ExportTraceServiceRequest request, CancellationToken cancellationToken)
        => ValueTask.FromResult(request.ResourceSpans.Count > 0);

    public override ValueTask<bool> ShouldSampleMetricsAsync(OpenTelemetryHandlerContext context, ExportMetricsServiceRequest request, CancellationToken cancellationToken)
        => ValueTask.FromResult(request.ResourceMetrics.Count > 0);
}
```
<!-- endref -->

Return `true` to keep the request, `false` to drop it. A dropped request is not passed to the handlers, and the client
still gets a success response. To tell the client that records were rejected, call `context.PartialSuccess.Reject`, as a
handler does: see [Custom handlers](04-CustomHandlers.md#report-rejected-records).

## Register a sampler

Add the sampler to `Samplers`, in the `configure` argument of `AddOpenTelemetryReceiver` or
`AddInMemoryOpenTelemetryReceiver`:

<!-- ref:../../tests/Meziantou.Framework.OpenTelemetry.Tests/OpenTelemetryReceiverTests.cs;region=OpenTelemetryCollector_AddSampler;format=md-fenced-code-block;dedent=true -->
```csharp
var builder = WebApplication.CreateBuilder();
builder.Services.AddInMemoryOpenTelemetryReceiver(options =>
{
    options.Samplers.Add(new KeepOnlyNonEmptyRequestsSampler());
});
```
<!-- endref -->

- The samplers run in the order of the list, before the handlers. The first one that returns `false` drops the request:
  the next ones are not called.
- The list is read once, when the receiver starts. Samplers added afterwards are ignored.
- A sampler is shared by all the requests, which can run in parallel: its state must be thread-safe.

## Sample whole traces

A request sampler only sees the spans of one request. The spans of a trace often arrive in several requests, from
several services, so a request sampler cannot decide on a trace as a whole. `OpenTelemetryTailSampler` buffers the
spans of each trace until the trace is complete, then decides. See [Tail sampling](07-TailSampling.md).
