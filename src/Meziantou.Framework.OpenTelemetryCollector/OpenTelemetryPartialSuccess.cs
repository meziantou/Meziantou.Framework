namespace Meziantou.Framework.OpenTelemetryCollector;

/// <summary>Collects the records rejected while handling an OTLP export request, so they can be reported back to the client through the OTLP <c>partial_success</c> response field.</summary>
/// <remarks>
/// Samplers and handlers can call <see cref="Reject(long, string?)"/> to report that some records could not be accepted.
/// Rejected records are aggregated across all samplers and handlers of a single request.
/// <para>
/// Calls made after the response has been sent are ignored. This happens when a trace is dispatched by
/// <see cref="OpenTelemetryTailSampler"/> because the spans are buffered and dispatched after the originating request completed.
/// </para>
/// </remarks>
public sealed class OpenTelemetryPartialSuccess
{
    /// <summary>An instance that discards everything reported to it.</summary>
    internal static OpenTelemetryPartialSuccess Discarded { get; } = new(discard: true);

    private readonly Lock _gate = new();
    private readonly bool _discard;

    private long _rejectedCount;
    private string? _errorMessage;

    internal OpenTelemetryPartialSuccess()
    {
    }

    private OpenTelemetryPartialSuccess(bool discard)
    {
        _discard = discard;
    }

    /// <summary>Gets the number of records rejected so far.</summary>
    public long RejectedCount
    {
        get
        {
            lock (_gate)
            {
                return _rejectedCount;
            }
        }
    }

    /// <summary>Gets the aggregated error message describing why records were rejected, or <see langword="null"/> when no message was reported.</summary>
    public string? ErrorMessage
    {
        get
        {
            lock (_gate)
            {
                return _errorMessage;
            }
        }
    }

    /// <summary>Reports that <paramref name="count"/> records were rejected.</summary>
    /// <param name="count">The number of rejected records. Can be <c>0</c> to report a warning without rejecting any record.</param>
    /// <param name="errorMessage">An optional human-readable message describing why the records were rejected.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    public void Reject(long count, string? errorMessage = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (_discard || (count is 0 && string.IsNullOrEmpty(errorMessage)))
            return;

        lock (_gate)
        {
            _rejectedCount += count;
            if (!string.IsNullOrEmpty(errorMessage))
            {
                _errorMessage = string.IsNullOrEmpty(_errorMessage) ? errorMessage : _errorMessage + "; " + errorMessage;
            }
        }
    }

    internal bool TryGetResult(out long rejectedCount, out string errorMessage)
    {
        lock (_gate)
        {
            rejectedCount = _rejectedCount;
            errorMessage = _errorMessage ?? "";
            return rejectedCount > 0 || errorMessage.Length > 0;
        }
    }
}
