using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

public partial class Window
{
    /// <summary>The long animation frame of the window, while its tasks run (https://w3c.github.io/long-animation-frames/#current-frame-timing-info).</summary>
    internal FrameTimingInfo? CurrentFrameTimingInfo { get; set; }

    private readonly SortedDictionary<uint, FrameRequestCallback> _animationFrameCallbacks = [];
    private uint _animationFrameCallbackIdentifier;

    // ----------------------------------------------------------------------------------------------------------------
    // Animation frames (https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#animation-frames)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-animationframeprovider-requestanimationframe:
    /// runs the callback at the next frame. There is no rendering: the frames run at a fixed rate (60 frames per second)
    /// measured with <see cref="DomEventLoop.TimeProvider"/>.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-animationframeprovider-requestanimationframe"/>
    [IdlMember("requestAnimationFrame", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-animationframeprovider-requestanimationframe")]
    public uint RequestAnimationFrame(FrameRequestCallback callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        // https://wicg.github.io/soft-navigations/#sec-interaction-context-intro: the callback runs in the interaction context of the caller
        if (InteractionContext.Current is { } interactionContext)
        {
            var innerCallback = callback;
            callback = time =>
            {
                using var scope = InteractionContext.Enter(interactionContext);
                innerCallback(time);
            };
        }

        _animationFrameCallbackIdentifier++;
        _animationFrameCallbacks[_animationFrameCallbackIdentifier] = callback;
        EventLoop.RequestAnimationFrame(this);
        return _animationFrameCallbackIdentifier;
    }

    /// <summary>The document of the window is restored from the back/forward cache: the animation frame callbacks that were not run while it was cached run at the next frame.</summary>
    internal void RequestPendingAnimationFrames()
    {
        if (_animationFrameCallbacks.Count > 0)
        {
            EventLoop.RequestAnimationFrame(this);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#animationframeprovider-cancelanimationframe</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#animationframeprovider-cancelanimationframe"/>
    [IdlMember("cancelAnimationFrame", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#animationframeprovider-cancelanimationframe")]
    public void CancelAnimationFrame(uint handle) => _animationFrameCallbacks.Remove(handle);

    /// <summary>https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#run-the-animation-frame-callbacks</summary>
    internal void RunAnimationFrameCallbacks(long frameTimestamp)
    {
        if (_animationFrameCallbacks.Count == 0 || !Document.IsFullyActive)
            return;

        var now = ToRelativeHighResolutionTime(frameTimestamp);
        var eventLoop = EventLoop;

        // The callbacks run in the long animation frame of the window, whatever the realm of their functions
        var callbackOwner = eventLoop.CallbackOwnerWindow;
        eventLoop.CallbackOwnerWindow = this;
        try
        {
            foreach (var handle in _animationFrameCallbacks.Keys.ToArray())
            {
                if (!_animationFrameCallbacks.Remove(handle, out var callback))
                    continue;

                try
                {
                    callback(now);
                }
#pragma warning disable CA1031 // The exceptions of the callbacks are reported
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    ScriptErrors.Report(ex, this);
                }
            }
        }
        finally
        {
            eventLoop.CallbackOwnerWindow = callbackOwner;
        }

        eventLoop.PerformMicrotaskCheckpoint();
    }
}
