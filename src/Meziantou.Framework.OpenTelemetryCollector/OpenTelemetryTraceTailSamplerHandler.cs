using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;

namespace Meziantou.Framework.OpenTelemetryCollector;

internal sealed class OpenTelemetryTraceTailSamplerHandler(TimeProvider timeProvider)
{
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly System.Threading.Lock _gate = new();

    private readonly Dictionary<string, BufferedTraceState> _traces = new(StringComparer.Ordinal);

    // Traces that exceeded their own span limit while using OpenTelemetryTailBufferOverflowPolicy.DropWholeTrace.
    // The decision must be remembered, otherwise the next batch of the same trace starts buffering again and the
    // trace is emitted as a set of fragments instead of being dropped. The value is the last time a span was seen.
    private readonly Dictionary<string, DateTimeOffset> _droppedTraces = new(StringComparer.Ordinal);

    private int _bufferedSpanCount;

    public async ValueTask HandleAsync(
        OpenTelemetryHandlerContext context,
        ExportTraceServiceRequest request,
        OpenTelemetryTailSampler tailSampling,
        Func<OpenTelemetryHandlerContext, ExportTraceServiceRequest, CancellationToken, ValueTask> acceptedTraceHandler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(tailSampling);
        ArgumentNullException.ThrowIfNull(acceptedTraceHandler);

        cancellationToken.ThrowIfCancellationRequested();

        var incomingByTrace = SplitByTraceId(request);
        if (incomingByTrace.Count is 0)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        var evaluations = new List<BufferedTraceEvaluation>();
        var rejectedSpanCount = 0;
        lock (_gate)
        {
            PurgeDroppedTraces(tailSampling, now);
            CollectTimedOutTraces(tailSampling, now, evaluations);

            foreach (var (traceId, entries) in incomingByTrace)
            {
                if (_droppedTraces.ContainsKey(traceId))
                {
                    _droppedTraces[traceId] = now;
                    rejectedSpanCount += entries.Count;
                    continue;
                }

                if (!_traces.TryGetValue(traceId, out var state))
                {
                    state = new BufferedTraceState(traceId, now);
                    _traces.Add(traceId, state);
                }

                state.LastContext = context;

                AppendEntries(state, entries);

                var (removedSpanCount, exceededPerTraceLimit) = ApplyCapacityPolicy(tailSampling, state);
                if (removedSpanCount > 0)
                {
                    rejectedSpanCount += removedSpanCount;
                    if (exceededPerTraceLimit && tailSampling.OverflowPolicy is OpenTelemetryTailBufferOverflowPolicy.DropWholeTrace)
                    {
                        _droppedTraces[traceId] = now;
                    }
                }

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

        if (rejectedSpanCount > 0)
        {
            context.PartialSuccess.Reject(rejectedSpanCount, "Spans were dropped because the tail sampling buffer is full");
        }

        await EvaluateAsync(evaluations, tailSampling, acceptedTraceHandler);
    }

    /// <summary>Evaluates the traces that reached <see cref="OpenTelemetryTailSampler.MaxTraceDuration"/> without having received their root span.</summary>
    /// <remarks>
    /// This is called by a background sweep. Without it, a buffered trace whose root span never arrives would only be
    /// released when another trace happens to be received, and would be kept in memory forever if traffic stops.
    /// </remarks>
    public async ValueTask FlushTimedOutTracesAsync(
        OpenTelemetryTailSampler tailSampling,
        Func<OpenTelemetryHandlerContext, ExportTraceServiceRequest, CancellationToken, ValueTask> acceptedTraceHandler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tailSampling);
        ArgumentNullException.ThrowIfNull(acceptedTraceHandler);

        cancellationToken.ThrowIfCancellationRequested();

        var now = _timeProvider.GetUtcNow();
        var evaluations = new List<BufferedTraceEvaluation>();
        lock (_gate)
        {
            PurgeDroppedTraces(tailSampling, now);
            CollectTimedOutTraces(tailSampling, now, evaluations);
        }

        if (evaluations.Count is 0)
        {
            return;
        }

        await EvaluateAsync(evaluations, tailSampling, acceptedTraceHandler);
    }

    private static async ValueTask EvaluateAsync(
        List<BufferedTraceEvaluation> evaluations,
        OpenTelemetryTailSampler tailSampling,
        Func<OpenTelemetryHandlerContext, ExportTraceServiceRequest, CancellationToken, ValueTask> acceptedTraceHandler)
    {
        // The spans of these traces were already removed from the buffer, so they are dispatched without a cancellation
        // token: aborting here would silently discard buffered data that belongs to other requests. For the same reason
        // a failing trace must not prevent the remaining ones from being dispatched.
        List<Exception>? errors = null;
        foreach (var evaluation in evaluations)
        {
            try
            {
                var accepted = true;
                if (tailSampling.ShouldSample is not null)
                {
                    accepted = await tailSampling.ShouldSample(evaluation.Context, CancellationToken.None);
                }

                if (!accepted)
                {
                    continue;
                }

                var acceptedRequest = CreateTraceRequest(evaluation.Entries);
                await acceptedTraceHandler(evaluation.Context.HandlerContext, acceptedRequest, CancellationToken.None);
            }
            catch (Exception ex)
            {
                errors ??= [];
                errors.Add(ex);
            }
        }

        if (errors is not null)
        {
            throw new AggregateException("One or more buffered traces could not be dispatched.", errors);
        }
    }

    private void PurgeDroppedTraces(OpenTelemetryTailSampler tailSampling, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tailSampling);

        if (_droppedTraces.Count is 0)
        {
            return;
        }

        var maxTraceDuration = tailSampling.MaxTraceDuration;
        List<string>? expiredTraceIds = null;
        foreach (var (traceId, lastSpanReceivedAt) in _droppedTraces)
        {
            if (now - lastSpanReceivedAt >= maxTraceDuration)
            {
                expiredTraceIds ??= [];
                expiredTraceIds.Add(traceId);
            }
        }

        if (expiredTraceIds is null)
        {
            return;
        }

        foreach (var traceId in expiredTraceIds)
        {
            _droppedTraces.Remove(traceId);
        }
    }

    private void CollectTimedOutTraces(OpenTelemetryTailSampler tailSampling, DateTimeOffset now, List<BufferedTraceEvaluation> evaluations)
    {
        ArgumentNullException.ThrowIfNull(tailSampling);
        ArgumentNullException.ThrowIfNull(evaluations);

        var maxTraceDuration = tailSampling.MaxTraceDuration;
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

    private (int RemovedSpanCount, bool ExceededPerTraceLimit) ApplyCapacityPolicy(OpenTelemetryTailSampler tailSampling, BufferedTraceState state)
    {
        ArgumentNullException.ThrowIfNull(tailSampling);
        ArgumentNullException.ThrowIfNull(state);

        var maxBufferedSpansPerTrace = Math.Max(0, tailSampling.MaxBufferedSpansPerTrace);
        var maxBufferedSpans = Math.Max(0, tailSampling.MaxBufferedSpans);

        var totalWithoutCurrentTrace = _bufferedSpanCount - state.SpanCount;
        var allowedByGlobalCapacity = Math.Max(0, maxBufferedSpans - totalWithoutCurrentTrace);
        var allowedSpansInTrace = Math.Min(maxBufferedSpansPerTrace, allowedByGlobalCapacity);
        if (state.SpanCount <= allowedSpansInTrace)
        {
            return (0, false);
        }

        // Only a trace larger than its own limit is oversized. Running out of global capacity is transient back
        // pressure caused by other traces, so it must not mark this trace as permanently dropped.
        var exceededPerTraceLimit = state.SpanCount > maxBufferedSpansPerTrace;
        var initialSpanCount = state.SpanCount;
        var spansToRemove = state.SpanCount - allowedSpansInTrace;
        switch (tailSampling.OverflowPolicy)
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
                throw new InvalidOperationException($"Unknown overflow policy: {tailSampling.OverflowPolicy}");
        }

        state.HasRootSpan = ContainsRootSpan(state.Entries);
        return (initialSpanCount - state.SpanCount, exceededPerTraceLimit);
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
