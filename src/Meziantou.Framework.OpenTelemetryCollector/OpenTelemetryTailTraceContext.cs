using OpenTelemetry.Proto.Trace.V1;

namespace Meziantou.Framework.OpenTelemetryCollector;

public sealed class OpenTelemetryTailTraceContext
{
    internal OpenTelemetryTailTraceContext(OpenTelemetryHandlerContext handlerContext, string traceId, IReadOnlyList<Span> spans, Span? rootSpan, bool timedOut, DateTimeOffset firstSpanReceivedAt, DateTimeOffset evaluationTime)
    {
        HandlerContext = handlerContext;
        TraceId = traceId;
        Spans = spans;
        RootSpan = rootSpan;
        IsRootSpanObserved = rootSpan is not null;
        TimedOut = timedOut;
        FirstSpanReceivedAt = firstSpanReceivedAt;
        EvaluationTime = evaluationTime;
    }

    public OpenTelemetryHandlerContext HandlerContext { get; }

    public string TraceId { get; }

    public IReadOnlyList<Span> Spans { get; }

    public Span? RootSpan { get; }

    public bool IsRootSpanObserved { get; }

    public bool TimedOut { get; }

    public DateTimeOffset FirstSpanReceivedAt { get; }

    public DateTimeOffset EvaluationTime { get; }
}
