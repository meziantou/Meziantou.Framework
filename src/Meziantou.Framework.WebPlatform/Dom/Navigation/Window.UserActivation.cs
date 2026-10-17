namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The user activation of the window (https://html.spec.whatwg.org/multipage/interaction.html#tracking-user-activation):
/// the timestamps of the last activation-triggering input event (e.g. a trusted <c>mousedown</c> or <c>keydown</c>), which
/// gate the APIs that need a user gesture.
/// </summary>
public partial class Window
{
    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#transient-activation-duration</summary>
    private static readonly TimeSpan TransientActivationDuration = TimeSpan.FromSeconds(5);

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/interaction.html#last-activation-timestamp, as a timestamp of the time provider
    /// of the event loop: <see cref="long.MaxValue"/> is positive infinity (never activated), <see cref="long.MinValue"/>
    /// negative infinity (the activation was consumed).
    /// </summary>
    private long _lastActivationTimestamp = long.MaxValue;

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#last-history-action-activation-timestamp</summary>
    private long _lastHistoryActionActivationTimestamp = long.MaxValue;

    private long CurrentTimestamp => EventLoop.TimeProvider.GetTimestamp();

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#sticky-activation</summary>
    internal bool HasStickyActivation => _lastActivationTimestamp != long.MaxValue && CurrentTimestamp >= _lastActivationTimestamp;

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#transient-activation</summary>
    internal bool HasTransientActivation
    {
        get
        {
            if (_lastActivationTimestamp is long.MaxValue or long.MinValue)
                return false;

            var now = CurrentTimestamp;
            return now >= _lastActivationTimestamp && EventLoop.TimeProvider.GetElapsedTime(_lastActivationTimestamp, now) < TransientActivationDuration;
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#history-action-activation</summary>
    internal bool HasHistoryActionActivation => _lastActivationTimestamp is not (long.MaxValue or long.MinValue) && _lastActivationTimestamp != _lastHistoryActionActivationTimestamp;

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/interaction.html#activation-notification: the window, the windows of the
    /// ancestor navigables and the windows of the same-origin descendant navigables get the activation, and their close
    /// watcher managers are notified (the next close watcher can start a new group).
    /// </summary>
    internal static void NotifyActivation(Document document)
    {
        if (document.RelevantWindow is not { } window || !ReferenceEquals(window.Document, document))
            return;

        var now = window.CurrentTimestamp;
        window._lastActivationTimestamp = now;
        window.CloseWatcherManager.NotifyAboutUserActivation();
        for (var navigable = document.BrowsingContext?.Parent; navigable is not null; navigable = navigable.Parent)
        {
            if (navigable.Document.RelevantWindow is { } ancestorWindow)
            {
                ancestorWindow._lastActivationTimestamp = now;
                ancestorWindow.CloseWatcherManager.NotifyAboutUserActivation();
            }
        }

        if (document.BrowsingContext is { } browsingContext)
        {
            foreach (var descendant in browsingContext.GetDescendantNavigables())
            {
                if (descendant.Document.RelevantWindow is { } descendantWindow && OriginRecord.IsSameOrigin(descendant.Document.Origin, document.Origin))
                {
                    descendantWindow._lastActivationTimestamp = now;
                    descendantWindow.CloseWatcherManager.NotifyAboutUserActivation();
                }
            }
        }
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/interaction.html#consume-user-activation: the windows of the navigables of the
    /// top-level traversable lose their transient activation.
    /// </summary>
    internal void ConsumeUserActivation()
    {
        if (BrowsingContext is not { } browsingContext)
            return;

        var top = browsingContext;
        while (top.Parent is { } parent)
        {
            top = parent;
        }

        foreach (var navigable in top.GetDescendantNavigables().Prepend(top))
        {
            if (navigable.Document.RelevantWindow is { } window && window._lastActivationTimestamp != long.MaxValue)
            {
                window._lastActivationTimestamp = long.MinValue;
            }
        }
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/interaction.html#consume-history-action-user-activation: the windows of the
    /// navigables of the top-level traversable lose their history-action activation.
    /// </summary>
    internal void ConsumeHistoryActionUserActivation()
    {
        if (BrowsingContext is not { } browsingContext)
            return;

        var top = browsingContext;
        while (top.Parent is { } parent)
        {
            top = parent;
        }

        foreach (var navigable in top.GetDescendantNavigables().Prepend(top))
        {
            if (navigable.Document.RelevantWindow is { } window)
            {
                window._lastHistoryActionActivationTimestamp = window._lastActivationTimestamp;
            }
        }
    }

    /// <summary>
    /// Whether a trusted event is an activation triggering input event
    /// (https://html.spec.whatwg.org/multipage/interaction.html#activation-triggering-input-event): a <c>keydown</c> that is
    /// not the Escape key, a <c>mousedown</c>, a <c>pointerdown</c> of a mouse, a <c>pointerup</c> of another pointer, or a
    /// <c>touchend</c>.
    /// </summary>
    internal static bool IsActivationTriggeringInputEvent(Event @event) => @event.IsTrusted && @event.Type switch
    {
        "keydown" => @event is not KeyboardEvent { Key: "Escape" },
        "mousedown" or "touchend" => true,
        "pointerdown" => @event is PointerEvent { PointerType: "mouse" },
        "pointerup" => @event is PointerEvent { PointerType: not "mouse" },
        _ => false,
    };
}
