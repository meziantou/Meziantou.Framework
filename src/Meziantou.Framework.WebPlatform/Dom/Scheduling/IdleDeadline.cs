namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The callback of <see cref="Window.RequestIdleCallback"/> (https://w3c.github.io/requestidlecallback/#the-idlerequestcallback-callback).</summary>
/// <param name="deadline">The time remaining in the idle period, and whether the callback runs because its timeout elapsed.</param>
/// <seealso href="https://w3c.github.io/requestidlecallback/#the-idlerequestcallback-callback"/>
[IdlInterface("IdleRequestCallback", "https://w3c.github.io/requestidlecallback/#the-idlerequestcallback-callback")]
public delegate void IdleRequestCallback(IdleDeadline deadline);

/// <summary>The options of <see cref="Window.RequestIdleCallback"/> (https://w3c.github.io/requestidlecallback/#the-idlerequestoptions-dictionary).</summary>
/// <seealso href="https://w3c.github.io/requestidlecallback/#the-idlerequestoptions-dictionary"/>
[IdlInterface("IdleRequestOptions", "https://w3c.github.io/requestidlecallback/#the-idlerequestoptions-dictionary")]
public sealed class IdleRequestOptions
{
    /// <summary>
    /// Gets the time, in milliseconds, after which the callback is queued as a task when no idle period ran it; null or 0
    /// for no timeout.
    /// </summary>
    /// <seealso href="https://w3c.github.io/requestidlecallback/#dom-idlerequestoptions-timeout"/>
    [IdlMember("timeout", "https://w3c.github.io/requestidlecallback/#dom-idlerequestoptions-timeout")]
    public uint? Timeout { get; init; }
}

/// <summary>
/// The deadline of an idle callback (https://w3c.github.io/requestidlecallback/#the-idledeadline-interface): the time
/// remaining in the idle period that runs the callback.
/// </summary>
/// <seealso href="https://w3c.github.io/requestidlecallback/#the-idledeadline-interface"/>
[IdlInterface("IdleDeadline", "https://w3c.github.io/requestidlecallback/#the-idledeadline-interface")]
public sealed class IdleDeadline
{
    private readonly Window _window;
    private readonly Func<long>? _getDeadline;

    internal IdleDeadline(Window window, Func<long>? getDeadline, bool didTimeout)
    {
        _window = window;
        _getDeadline = getDeadline;
        DidTimeout = didTimeout;
    }

    /// <summary>
    /// Returns the time remaining in the idle period, in milliseconds (https://w3c.github.io/requestidlecallback/#dom-idledeadline-timeremaining):
    /// 0 when the idle period ended, or when the callback runs because its timeout elapsed. The deadline is computed again
    /// at each call: a timer or an animation frame requested by the callback ends the idle period sooner.
    /// </summary>
    /// <seealso href="https://w3c.github.io/requestidlecallback/#dom-idledeadline-timeremaining"/>
    [IdlMember("timeRemaining", "https://w3c.github.io/requestidlecallback/#dom-idledeadline-timeremaining")]
    public double TimeRemaining()
    {
        if (_getDeadline is null)
            return 0;

        var now = _window.GetCurrentHighResolutionTime();
        var deadline = _window.ToRelativeHighResolutionTime(_getDeadline());
        return Math.Max(0, deadline - now);
    }

    /// <summary>Gets whether the callback runs because its timeout elapsed (https://w3c.github.io/requestidlecallback/#dom-idledeadline-didtimeout).</summary>
    /// <seealso href="https://w3c.github.io/requestidlecallback/#dom-idledeadline-didtimeout"/>
    [IdlMember("didTimeout", "https://w3c.github.io/requestidlecallback/#dom-idledeadline-didtimeout")]
    public bool DidTimeout { get; }
}
