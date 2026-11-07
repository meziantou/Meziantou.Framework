using System.Diagnostics;

namespace Meziantou.Framework.Diagnostics;

/// <summary>Options for <see cref="ScopedActivityListener"/>.</summary>
public sealed class ScopedActivityListenerOptions
{
    /// <summary>
    /// Gets or sets the predicate used for <see cref="ActivityListener.ShouldListenTo"/>. The default value listens to every source.
    /// </summary>
    /// <remarks>
    /// The predicate is evaluated once per <see cref="ActivitySource"/> and its result is cached by the runtime, so it must be a pure
    /// function of the source. In particular it cannot depend on the current asynchronous flow.
    /// </remarks>
    public Func<ActivitySource, bool>? ShouldListenTo { get; set; }

    /// <summary>
    /// Gets or sets the names of the <see cref="ActivitySource"/> to listen to, compared with <see cref="StringComparison.Ordinal"/>.
    /// This value is ignored when <see cref="ShouldListenTo"/> is set. Use <see cref="string.Empty"/> to listen to the activities created
    /// with <c>new Activity(name)</c>.
    /// </summary>
    public IReadOnlyList<string>? SourceNames { get; set; }

    /// <summary>
    /// Gets or sets the sampling result returned for the activities created in the scope. The default value is <see cref="ActivitySamplingResult.AllData"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="ActivitySamplingResult.AllDataAndRecorded"/> sets <see cref="ActivityTraceFlags.Recorded"/>, which is propagated to the
    /// downstream services through the <c>traceparent</c> header and changes their own sampling decisions.
    /// </remarks>
    public ActivitySamplingResult SamplingResult { get; set; } = ActivitySamplingResult.AllData;

    /// <summary>
    /// Gets or sets a value indicating whether the activities whose parent is captured are also captured, even when the asynchronous scope
    /// was lost. The default value is <see langword="true"/>.
    /// </summary>
    public bool CaptureChildActivities { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of completed activities kept in <see cref="ScopedActivityListener.Activities"/>. When the limit is
    /// reached, the oldest activities are dropped. The default value is <see cref="int.MaxValue"/>.
    /// </summary>
    public int MaxActivityCount { get; set; } = int.MaxValue;
}
