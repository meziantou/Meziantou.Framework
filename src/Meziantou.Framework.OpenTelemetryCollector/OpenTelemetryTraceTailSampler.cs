using Microsoft.Extensions.Options;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;

namespace Meziantou.Framework.OpenTelemetryCollector;

internal sealed class OpenTelemetryTraceTailSampler(IOptions<OpenTelemetryReceiverOptions> optionsAccessor)
{
#if NET9_0_OR_GREATER
    private readonly System.Threading.Lock _gate = new();
#else
#pragma warning disable MA0158 // System.Threading.Lock is not available on net8
    private readonly object _gate = new();
#pragma warning restore MA0158
#endif

    private readonly Dictionary<string, BufferedTraceState> _traces = new(StringComparer.Ordinal);
    private readonly OpenTelemetryReceiverOptions _options = optionsAccessor.Value;
    private int _bufferedSpanCount;

    public async ValueTask HandleAsync(
        OpenTelemetryHandlerContext context,
        ExportTraceServiceRequest request,
        Func<OpenTelemetryHandlerContext, ExportTraceServiceRequest, CancellationToken, ValueTask> acceptedTraceHandler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(acceptedTraceHandler);

        cancellationToken.ThrowIfCancellationRequested();

        var incomingByTrace = SplitByTraceId(request);
        if (incomingByTrace.Count is 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var evaluations = new List<BufferedTraceEvaluation>();
        lock (_gate)
        {
            CollectTimedOutTraces(now, evaluations);

            foreach (var (traceId, entries) in incomingByTrace)
            {
                if (!_traces.TryGetValue(traceId, out var state))
                {
                    state = new BufferedTraceState(traceId, now);
                    _traces.Add(traceId, state);
                }

                state.LastContext = context;

                AppendEntries(state, entries);
                ApplyCapacityPolicy(state);

                if (state.SpanCount is 0)
                {
                    _traces.Remove(traceId);
                    continue;
                }

                if (state.HasRootSpan)
                {
                    evaluations.Add(CreateEvaluation(state, timedOut: false, now));
                    RemoveTrace(traceId, state);
                }
            }
        }

        foreach (var evaluation in evaluations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var accepted = true;
            if (_options.TailSampling.Filter is not null)
            {
                accepted = await _options.TailSampling.Filter(evaluation.Context, cancellationToken);
            }

            if (!accepted)
            {
                continue;
            }

            var acceptedRequest = CreateTraceRequest(evaluation.Entries);
            await acceptedTraceHandler(evaluation.Context.HandlerContext, acceptedRequest, cancellationToken);
        }
    }

    private void CollectTimedOutTraces(DateTimeOffset now, List<BufferedTraceEvaluation> evaluations)
    {
        ArgumentNullException.ThrowIfNull(evaluations);

        var maxTraceDuration = _options.TailSampling.MaxTraceDuration;
        var traceIdsToEvaluate = new List<string>();
        foreach (var (traceId, state) in _traces)
        {
            if (now - state.FirstSpanReceivedAt >= maxTraceDuration)
            {
                traceIdsToEvaluate.Add(traceId);
            }
        }

        foreach (var traceId in traceIdsToEvaluate)
        {
            if (_traces.TryGetValue(traceId, out var state))
            {
                evaluations.Add(CreateEvaluation(state, timedOut: true, now));
                RemoveTrace(traceId, state);
            }
        }
    }

    private void AppendEntries(BufferedTraceState state, List<BufferedSpanEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(entries);

        state.Entries.AddRange(entries);
        state.SpanCount += entries.Count;
        _bufferedSpanCount += entries.Count;
        state.HasRootSpan = ContainsRootSpan(state.Entries);
    }

    private void ApplyCapacityPolicy(BufferedTraceState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var maxBufferedSpansPerTrace = Math.Max(0, _options.TailSampling.MaxBufferedSpansPerTrace);
        var maxBufferedSpans = Math.Max(0, _options.TailSampling.MaxBufferedSpans);

        var totalWithoutCurrentTrace = _bufferedSpanCount - state.SpanCount;
        var allowedByGlobalCapacity = Math.Max(0, maxBufferedSpans - totalWithoutCurrentTrace);
        var allowedSpansInTrace = Math.Min(maxBufferedSpansPerTrace, allowedByGlobalCapacity);
        if (state.SpanCount <= allowedSpansInTrace)
        {
            return;
        }

        var spansToRemove = state.SpanCount - allowedSpansInTrace;
        switch (_options.TailSampling.OverflowPolicy)
        {
            case OpenTelemetryTailBufferOverflowPolicy.DropWholeTrace:
                TrimFromStart(state, state.SpanCount);
                break;
            case OpenTelemetryTailBufferOverflowPolicy.DropOldestSpans:
                TrimFromStart(state, spansToRemove);
                break;
            case OpenTelemetryTailBufferOverflowPolicy.DropNewestSpans:
                TrimFromEnd(state, spansToRemove);
                break;
            default:
                throw new InvalidOperationException($"Unknown overflow policy: {_options.TailSampling.OverflowPolicy}");
        }

        state.HasRootSpan = ContainsRootSpan(state.Entries);
    }

    private void TrimFromStart(BufferedTraceState state, int spanCount)
    {
        ArgumentNullException.ThrowIfNull(state);

        var count = Math.Min(state.SpanCount, spanCount);
        if (count <= 0)
        {
            return;
        }

        state.Entries.RemoveRange(0, count);
        state.SpanCount -= count;
        _bufferedSpanCount -= count;
    }

    private void TrimFromEnd(BufferedTraceState state, int spanCount)
    {
        ArgumentNullException.ThrowIfNull(state);

        var count = Math.Min(state.SpanCount, spanCount);
        if (count <= 0)
        {
            return;
        }

        state.Entries.RemoveRange(state.Entries.Count - count, count);
        state.SpanCount -= count;
        _bufferedSpanCount -= count;
    }

    private void RemoveTrace(string traceId, BufferedTraceState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        _traces.Remove(traceId);
        _bufferedSpanCount -= state.SpanCount;
    }

    private static BufferedTraceEvaluation CreateEvaluation(BufferedTraceState state, bool timedOut, DateTimeOffset evaluationTime)
    {
        ArgumentNullException.ThrowIfNull(state);

        var spans = state.Entries.Select(static span => span.Span.Clone()).ToArray();
        var rootSpan = spans.FirstOrDefault(IsRootSpan);
        var context = new OpenTelemetryTailTraceContext(
            state.LastContext,
            state.TraceId,
            spans,
            rootSpan,
            timedOut,
            state.FirstSpanReceivedAt,
            evaluationTime);

        return new BufferedTraceEvaluation(context, [.. state.Entries]);
    }

    private static ExportTraceServiceRequest CreateTraceRequest(IReadOnlyList<BufferedSpanEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var request = new ExportTraceServiceRequest();
        foreach (var entry in entries)
        {
            var resourceSpans = new ResourceSpans
            {
                Resource = entry.Resource.Clone(),
                SchemaUrl = entry.ResourceSchemaUrl,
            };

            var scopeSpans = new ScopeSpans
            {
                Scope = entry.Scope.Clone(),
                SchemaUrl = entry.ScopeSchemaUrl,
            };

            scopeSpans.Spans.Add(entry.Span.Clone());
            resourceSpans.ScopeSpans.Add(scopeSpans);
            request.ResourceSpans.Add(resourceSpans);
        }

        return request;
    }

    private static Dictionary<string, List<BufferedSpanEntry>> SplitByTraceId(ExportTraceServiceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var traces = new Dictionary<string, List<BufferedSpanEntry>>(StringComparer.Ordinal);
        foreach (var resourceSpans in request.ResourceSpans)
        {
            var resource = resourceSpans.Resource?.Clone() ?? new Resource();

            foreach (var scopeSpans in resourceSpans.ScopeSpans)
            {
                var scope = scopeSpans.Scope?.Clone() ?? new InstrumentationScope();

                foreach (var span in scopeSpans.Spans)
                {
                    if (span.TraceId.IsEmpty)
                    {
                        continue;
                    }

                    var traceId = Convert.ToHexString(span.TraceId.ToByteArray());
                    if (!traces.TryGetValue(traceId, out var traceEntries))
                    {
                        traceEntries = [];
                        traces.Add(traceId, traceEntries);
                    }

                    traceEntries.Add(new BufferedSpanEntry(resource, resourceSpans.SchemaUrl, scope, scopeSpans.SchemaUrl, span.Clone()));
                }
            }
        }

        return traces;
    }

    private static bool ContainsRootSpan(List<BufferedSpanEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return entries.Any(static span => IsRootSpan(span.Span));
    }

    private static bool IsRootSpan(global::OpenTelemetry.Proto.Trace.V1.Span span)
    {
        ArgumentNullException.ThrowIfNull(span);

        return span.ParentSpanId.IsEmpty;
    }

    private sealed class BufferedTraceState(string traceId, DateTimeOffset firstSpanReceivedAt)
    {
        public string TraceId { get; } = traceId;

        public DateTimeOffset FirstSpanReceivedAt { get; } = firstSpanReceivedAt;

        public OpenTelemetryHandlerContext LastContext { get; set; }

        public List<BufferedSpanEntry> Entries { get; } = [];

        public int SpanCount { get; set; }

        public bool HasRootSpan { get; set; }
    }

    private sealed record BufferedSpanEntry(Resource Resource, string ResourceSchemaUrl, InstrumentationScope Scope, string ScopeSchemaUrl, global::OpenTelemetry.Proto.Trace.V1.Span Span);

    private sealed record BufferedTraceEvaluation(OpenTelemetryTailTraceContext Context, IReadOnlyList<BufferedSpanEntry> Entries);
}
