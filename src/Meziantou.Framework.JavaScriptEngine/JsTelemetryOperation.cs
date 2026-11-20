using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// An operation traced by a span and/or measured by a histogram of <see cref="JsEngineTelemetry"/>. It allocates nothing
/// when nothing listens. Complete it with <see cref="Stop"/>, or with <see cref="Fail"/> in an exception filter:
/// <c>catch (Exception ex) when (operation.Fail(ex)) { throw; }</c>.
/// </summary>
internal readonly ref struct JsTelemetryOperation
{
    private readonly Activity? _activity;
    private readonly Histogram<double>? _histogram;
    private readonly JsCodeKind _kind;
    private readonly long _startTimestamp;

    private JsTelemetryOperation(Activity? activity, Histogram<double>? histogram, JsCodeKind kind, long startTimestamp)
    {
        _activity = activity;
        _histogram = histogram;
        _kind = kind;
        _startTimestamp = startTimestamp;
    }

    /// <summary>Starts an operation.</summary>
    /// <param name="activityName">The name of the span, or null for no span.</param>
    /// <param name="histogram">The histogram of the duration, or null for no measure.</param>
    /// <param name="kind">The kind of code.</param>
    /// <param name="url">The URL of the source text (a tag of the span).</param>
    /// <param name="sourceLength">The length of the source text (a tag of the span), or -1.</param>
    public static JsTelemetryOperation Start(string? activityName, Histogram<double>? histogram, JsCodeKind kind, string? url = null, int sourceLength = -1)
    {
        Activity? activity = null;
        if (activityName is not null && JsEngineTelemetry.ActivitySource.HasListeners())
        {
            activity = JsEngineTelemetry.ActivitySource.StartActivity(activityName, ActivityKind.Internal);
            if (activity is { IsAllDataRequested: true })
            {
                activity.SetTag(JsEngineTelemetry.CodeKindTagName, JsEngineTelemetry.GetTagValue(kind));
                if (url is not null)
                {
                    activity.SetTag(JsEngineTelemetry.SourceUrlTagName, url);
                }

                if (sourceLength >= 0)
                {
                    activity.SetTag(JsEngineTelemetry.SourceLengthTagName, sourceLength);
                }
            }
        }

        if (histogram is not { Enabled: true })
        {
            histogram = null;
        }

        return new JsTelemetryOperation(activity, histogram, kind, histogram is null ? 0 : Stopwatch.GetTimestamp());
    }

    /// <summary>Completes the operation successfully.</summary>
    public void Stop()
    {
        Record(errorType: null);
        _activity?.Stop();
    }

    /// <summary>
    /// Completes the operation with an error (thrown, or reported otherwise, such as the error of a module evaluation). It
    /// returns false, to be used in an exception filter that never catches the exception.
    /// </summary>
    public bool Fail(Exception exception)
    {
        var errorType = exception.GetType().FullName;
        Record(errorType);
        if (_activity is not null)
        {
            _activity.SetStatus(ActivityStatusCode.Error, exception.Message);
            _activity.SetTag(JsEngineTelemetry.ErrorTypeTagName, errorType);
            if (exception is JsTerminationException termination)
            {
                _activity.SetTag(JsEngineTelemetry.TerminationReasonTagName, JsEngineTelemetry.GetTagValue(termination.Reason));
            }

            _activity.AddException(exception);
            _activity.Stop();
        }

        return false;
    }

    private void Record(string? errorType)
    {
        if (_histogram is null)
            return;

        var duration = Stopwatch.GetElapsedTime(_startTimestamp).TotalSeconds;
        var kind = new KeyValuePair<string, object?>(JsEngineTelemetry.CodeKindTagName, JsEngineTelemetry.GetTagValue(_kind));
        if (errorType is null)
        {
            _histogram.Record(duration, kind);
        }
        else
        {
            _histogram.Record(duration, kind, new KeyValuePair<string, object?>(JsEngineTelemetry.ErrorTypeTagName, errorType));
        }
    }
}
