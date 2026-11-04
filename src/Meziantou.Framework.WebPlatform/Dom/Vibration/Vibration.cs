namespace Meziantou.Framework.WebPlatform.Dom;

// The Vibration API (https://w3c.github.io/vibration/)
public sealed partial class Navigator
{
    /// <summary>The maximum number of entries of a pattern (as Chromium).</summary>
    private const int MaxVibrationPatternLength = 99;

    /// <summary>The maximum duration of an entry of a pattern, in milliseconds (as Chromium).</summary>
    private const uint MaxVibrationDuration = 10_000;

    /// <summary>The timestamp at which the current vibration pattern ends, or null when the device does not vibrate.</summary>
    private long? _vibrationEndTimestamp;

    /// <summary>
    /// Vibrates the device with a pattern (https://w3c.github.io/vibration/#vibrate-method), with
    /// <see cref="BrowsingContextOptions.Vibrate"/>: the durations alternate between the vibrations and the pauses. Returns
    /// false when the document is hidden or has no sticky activation, true otherwise. An empty pattern, or a pattern of
    /// zeros, cancels the current vibration.
    /// </summary>
    /// <param name="pattern">The duration of the vibration, in milliseconds.</param>
    /// <seealso href="https://w3c.github.io/vibration/#dom-navigator-vibrate"/>
    [IdlMember("vibrate", "https://w3c.github.io/vibration/#dom-navigator-vibrate")]
    public bool Vibrate(uint pattern) => Vibrate([pattern]);

    /// <summary>
    /// Vibrates the device with a pattern (https://w3c.github.io/vibration/#vibrate-method), with
    /// <see cref="BrowsingContextOptions.Vibrate"/>: the durations alternate between the vibrations and the pauses. Returns
    /// false when the document is hidden or has no sticky activation, true otherwise. An empty pattern, or a pattern of
    /// zeros, cancels the current vibration.
    /// </summary>
    /// <param name="pattern">The durations of the vibrations and the pauses, in milliseconds.</param>
    /// <seealso href="https://w3c.github.io/vibration/#dom-navigator-vibrate"/>
    [IdlMember("vibrate", "https://w3c.github.io/vibration/#dom-navigator-vibrate")]
    public bool Vibrate(IReadOnlyList<uint> pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        var window = _window;
        if (!window.Document.IsFullyActive || window.Document.Hidden || !window.HasStickyActivation)
            return false;

        // Validate and normalize the pattern
        var durations = pattern.Take(MaxVibrationPatternLength).Select(duration => Math.Min(duration, MaxVibrationDuration)).ToList();
        if (durations.Count % 2 == 0 && durations.Count > 0)
        {
            durations.RemoveAt(durations.Count - 1);
        }

        if (durations.TrueForAll(duration => duration == 0))
        {
            CancelVibration();
            return true;
        }

        var timeProvider = window.EventLoop.TimeProvider;
        var total = durations.Aggregate(0L, (sum, duration) => sum + duration);
        _vibrationEndTimestamp = timeProvider.GetTimestamp() + (long)(total / 1000d * timeProvider.TimestampFrequency);
        window.Options.Vibrate?.Invoke(CallbackContext.For(window), durations.ConvertAll(duration => TimeSpan.FromMilliseconds(duration)));
        return true;
    }

    /// <summary>
    /// Cancels the current vibration, if any (https://w3c.github.io/vibration/#dfn-perform-vibration): called when a new
    /// pattern of zeros is requested and when the document becomes hidden.
    /// </summary>
    internal void CancelVibration()
    {
        if (_vibrationEndTimestamp is not { } end)
            return;

        _vibrationEndTimestamp = null;
        var window = _window;
        if (window.EventLoop.TimeProvider.GetTimestamp() < end)
        {
            window.Options.Vibrate?.Invoke(CallbackContext.For(window), []);
        }
    }
}
