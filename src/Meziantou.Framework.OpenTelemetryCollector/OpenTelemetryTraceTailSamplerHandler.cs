using Microsoft.Extensions.Logging;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;

namespace Meziantou.Framework.OpenTelemetryCollector;

internal sealed class OpenTelemetryTraceTailSamplerHandler(TimeProvider timeProvider, ILogger<OpenTelemetryTraceTailSamplerHandler> logger)
{
    // Upper bound on the number of remembered trace ids. Dropped traces expire after OpenTelemetryTailSampler.MaxTraceDuration,
    // but a client that keeps sending oversized traces would otherwise grow the map without any ceiling.
    private const int MaxDroppedTraceCount = 10_000;

    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<OpenTelemetryTraceTailSamplerHandler> _logger = logger;
    private readonly System.Threading.Lock _gate = new();

    private readonly Dictionary<string, BufferedTraceState> _traces = new(StringComparer.Ordinal);

    // Traces that were dropped as a whole, either because they exceeded their own span limit while using
    // OpenTelemetryTailBufferOverflowPolicy.DropWholeTrace, or because they were evicted to free global buffer capacity.
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
        int evictedSpanCount;
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

                var removedSpanCount = ApplyPerTraceCapacity(tailSampling, state);
                if (removedSpanCount > 0)
                {
                    rejectedSpanCount += removedSpanCount;
                    if (tailSampling.OverflowPolicy is OpenTelemetryTailBufferOverflowPolicy.DropWholeTrace)
                    {
                        MarkTraceAsDropped(traceId, now);
                    }
                }

                if (state.SpanCount is 0)
                {
                    RemoveTrace(traceId, state);
                    continue;
                }

                if (state.HasRootSpan)
                {
                    evaluations.Add(CreateEvaluation(state, timedOut: false, now));
                    RemoveTrace(traceId, state);
                }
            }

            evictedSpanCount = EnforceGlobalCapacity(tailSampling, now);
        }

        if (rejectedSpanCount > 0)
        {
            context.PartialSuccess.Reject(rejectedSpanCount, "Spans were dropped because the tail sampling buffer is full");
        }

        if (evictedSpanCount > 0)
        {
            // Evicted spans belong to traces buffered by other requests, so they are not reported to this client:
            // the partial_success field must only describe the records this request sent.
            _logger.LogWarning("Evicted {SpanCount} buffered spans to stay below the maximum number of buffered spans ({MaxBufferedSpans})", evictedSpanCount, tailSampling.MaxBufferedSpans);
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

    private async ValueTask EvaluateAsync(
        List<BufferedTraceEvaluation> evaluations,
        OpenTelemetryTailSampler tailSampling,
        Func<OpenTelemetryHandlerContext, ExportTraceServiceRequest, CancellationToken, ValueTask> acceptedTraceHandler)
    {
        // The spans of these traces were already removed from the buffer, so they are dispatched without a cancellation
        // token: aborting here would silently discard buffered data that belongs to other requests. For the same reason
        // a failure is logged instead of being thrown: buffered traces are dispatched outside of the request that
        // produced them, so a failing trace must neither stop the remaining ones nor fail whichever request happened to
        // trigger the flush.
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
                _logger.LogError(ex, "Cannot dispatch the buffered trace {TraceId}", evaluation.Context.TraceId);
            }
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

    private void MarkTraceAsDropped(string traceId, DateTimeOffset now)
    {
        _droppedTraces[traceId] = now;
        if (_droppedTraces.Count <= MaxDroppedTraceCount)
        {
            return;
        }

        // Forget the least recently seen ids. A batch is removed at once, so the scan is amortized over many additions.
        var countToRemove = _droppedTraces.Count - MaxDroppedTraceCount + (MaxDroppedTraceCount / 10);
        var expiredTraceIds = _droppedTraces
            .OrderBy(static pair => pair.Value)
            .Take(countToRemove)
            .Select(static pair => pair.Key)
            .ToArray();

        foreach (var expiredTraceId in expiredTraceIds)
        {
            _droppedTraces.Remove(expiredTraceId);
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

        // Only the new entries need to be scanned: a root span already observed cannot disappear by appending.
        if (!state.HasRootSpan)
        {
            state.HasRootSpan = ContainsRootSpan(entries);
        }
    }

    /// <summary>Applies <see cref="OpenTelemetryTailSampler.OverflowPolicy"/> to a trace that grew beyond <see cref="OpenTelemetryTailSampler.MaxBufferedSpansPerTrace"/>.</summary>
    /// <remarks>
    /// Only the per-trace limit is enforced here. Running out of global capacity is caused by other traces, so it must
    /// never truncate a trace that is within its own limit: it is handled by <see cref="EnforceGlobalCapacity"/>, which
    /// evicts whole traces instead.
    /// </remarks>
    private int ApplyPerTraceCapacity(OpenTelemetryTailSampler tailSampling, BufferedTraceState state)
    {
        ArgumentNullException.ThrowIfNull(tailSampling);
        ArgumentNullException.ThrowIfNull(state);

        var maxBufferedSpansPerTrace = Math.Max(0, tailSampling.MaxBufferedSpansPerTrace);
        if (state.SpanCount <= maxBufferedSpansPerTrace)
        {
            return 0;
        }

        var initialSpanCount = state.SpanCount;
        var spansToRemove = state.SpanCount - maxBufferedSpansPerTrace;
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

        // Trimming can remove the root span, so the whole buffer must be scanned again.
        state.HasRootSpan = ContainsRootSpan(state.Entries);
        return initialSpanCount - state.SpanCount;
    }

    /// <summary>Evicts whole traces, largest first, until the total number of buffered spans is within <see cref="OpenTelemetryTailSampler.MaxBufferedSpans"/>.</summary>
    /// <remarks>
    /// Evicting a deliberate victim keeps the buffer bounded without truncating whichever trace happens to be written
    /// next, and guarantees that new traces are still admitted once the buffer is full. The largest trace is evicted
    /// first because it frees the most capacity for the fewest evictions, and because it is the one causing the
    /// pressure: evicting the oldest instead would repeatedly sacrifice the trace closest to receiving its root span.
    /// Evicted traces are remembered so their later spans are dropped instead of being re-buffered and emitted as
    /// fragments.
    /// </remarks>
    private int EnforceGlobalCapacity(OpenTelemetryTailSampler tailSampling, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tailSampling);

        var maxBufferedSpans = Math.Max(0, tailSampling.MaxBufferedSpans);
        if (_bufferedSpanCount <= maxBufferedSpans)
        {
            return 0;
        }

        var victims = new List<BufferedTraceState>(_traces.Values);
        victims.Sort(static (x, y) =>
        {
            var result = y.SpanCount.CompareTo(x.SpanCount);
            return result is not 0 ? result : x.FirstSpanReceivedAt.CompareTo(y.FirstSpanReceivedAt);
        });

        var evictedSpanCount = 0;
        foreach (var victim in victims)
        {
            if (_bufferedSpanCount <= maxBufferedSpans)
            {
                break;
            }

            evictedSpanCount += victim.SpanCount;
            MarkTraceAsDropped(victim.TraceId, now);
            RemoveTrace(victim.TraceId, victim);
        }

        return evictedSpanCount;
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

        // The spans were cloned when they were buffered, so they are not shared with the request that produced them and
        // do not need to be cloned again: the sampler sees the very instances that are dispatched to the handlers.
        var spans = state.Entries.Select(static entry => entry.Span).ToArray();
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

    /// <summary>Rebuilds an export request from buffered spans, grouping them back by resource and instrumentation scope.</summary>
    /// <remarks>
    /// Emitting one <see cref="ResourceSpans"/> per span would multiply the payload by the number of spans and discard
    /// the grouping the client sent, which handlers legitimately rely on.
    /// </remarks>
    private static ExportTraceServiceRequest CreateTraceRequest(IReadOnlyList<BufferedSpanEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var request = new ExportTraceServiceRequest();
        var resourceSpansByKey = new Dictionary<(Resource Resource, string SchemaUrl), ResourceSpans>();
        var scopeSpansByKey = new Dictionary<(Resource Resource, string ResourceSchemaUrl, InstrumentationScope Scope, string ScopeSchemaUrl), ScopeSpans>();

        foreach (var entry in entries)
        {
            var resourceKey = (entry.Resource, entry.ResourceSchemaUrl);
            if (!resourceSpansByKey.TryGetValue(resourceKey, out var resourceSpans))
            {
                resourceSpans = new ResourceSpans
                {
                    Resource = entry.Resource.Clone(),
                    SchemaUrl = entry.ResourceSchemaUrl,
                };

                request.ResourceSpans.Add(resourceSpans);
                resourceSpansByKey.Add(resourceKey, resourceSpans);
            }

            var scopeKey = (entry.Resource, entry.ResourceSchemaUrl, entry.Scope, entry.ScopeSchemaUrl);
            if (!scopeSpansByKey.TryGetValue(scopeKey, out var scopeSpans))
            {
                scopeSpans = new ScopeSpans
                {
                    Scope = entry.Scope.Clone(),
                    SchemaUrl = entry.ScopeSchemaUrl,
                };

                resourceSpans.ScopeSpans.Add(scopeSpans);
                scopeSpansByKey.Add(scopeKey, scopeSpans);
            }

            scopeSpans.Spans.Add(entry.Span);
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
