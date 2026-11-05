namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The host hooks of <c>navigator.scheduling.isInputPending()</c> (https://wicg.github.io/is-input-pending/, <see cref="BrowsingContextOptions.PendingInput"/>).</summary>
public sealed class PendingInputOptions
{
    /// <summary>
    /// Gets or sets the callback that tells whether input events are waiting to be dispatched to a window
    /// (https://wicg.github.io/is-input-pending/#the-isinputpending-method): the tasks of the user interaction task source
    /// that the host has not given to the library yet (e.g. a click while a long script runs). The second argument is
    /// <c>includeContinuous</c>: when it is false, the continuous events (<c>mousemove</c>, <c>pointermove</c>,
    /// <c>pointerrawupdate</c>, <c>touchmove</c>, <c>wheel</c> and <c>drag</c>) are not considered. The callback must
    /// return false for the input dispatched to a cross-origin window, and when it is not sure that the input targets the
    /// window. The library checks that the document is fully active before calling it. The callback is invoked on the
    /// thread of the event loop, while a script runs: it must return quickly. The default is null: no input is pending.
    /// The first argument is the context of the window (see <see cref="CallbackContext"/>): its <see cref="CallbackContext.Window"/> is never null.
    /// </summary>
    public Func<CallbackContext, bool, bool>? IsInputPending { get; init; }
}

/// <summary>The options of <see cref="Scheduling.IsInputPending"/> (https://wicg.github.io/is-input-pending/#dom-isinputpendingoptions).</summary>
/// <seealso href="https://wicg.github.io/is-input-pending/#dom-isinputpendingoptions"/>
[IdlInterface("IsInputPendingOptions", "https://wicg.github.io/is-input-pending/#dom-isinputpendingoptions")]
public sealed class IsInputPendingOptions
{
    /// <summary>Gets or sets whether the continuous events (e.g. <c>mousemove</c>, <c>wheel</c>) are considered. The default is false.</summary>
    /// <seealso href="https://wicg.github.io/is-input-pending/#dom-isinputpendingoptions-includecontinuous"/>
    [IdlMember("includeContinuous", "https://wicg.github.io/is-input-pending/#dom-isinputpendingoptions-includecontinuous")]
    public bool IncludeContinuous { get; set; }
}

/// <summary>
/// The scheduling of a window (https://wicg.github.io/is-input-pending/#the-scheduling-interface, <c>navigator.scheduling</c>):
/// <see cref="IsInputPending"/> tells a long script whether it should yield to let the input events be dispatched.
/// </summary>
/// <seealso href="https://wicg.github.io/is-input-pending/#dom-scheduling"/>
[IdlInterface("Scheduling", "https://wicg.github.io/is-input-pending/#dom-scheduling")]
public sealed class Scheduling
{
    private readonly Window _window;

    internal Scheduling(Window window) => _window = window;

    /// <summary>
    /// Returns whether input events are waiting to be dispatched to the window (https://wicg.github.io/is-input-pending/#the-isinputpending-method),
    /// with <see cref="PendingInputOptions.IsInputPending"/>: false when the host does not set it, or when the document is
    /// not fully active.
    /// </summary>
    /// <param name="isInputPendingOptions">The options: whether the continuous events are considered.</param>
    /// <returns>Whether input is pending.</returns>
    /// <seealso href="https://wicg.github.io/is-input-pending/#dom-scheduling-isinputpending"/>
    [IdlMember("isInputPending", "https://wicg.github.io/is-input-pending/#dom-scheduling-isinputpending")]
    public bool IsInputPending(IsInputPendingOptions? isInputPendingOptions = null)
    {
        // "A user agent may return false for any reason": the input of a document that is not fully active is not dispatched
        if (!_window.Document.IsFullyActive || _window.Options.PendingInput?.IsInputPending is not { } isInputPending)
            return false;

        return isInputPending(CallbackContext.For(_window), isInputPendingOptions?.IncludeContinuous ?? false);
    }
}

// Early detection of input events (https://wicg.github.io/is-input-pending/)
public sealed partial class Navigator
{
    private Scheduling? _scheduling;

    /// <summary>Gets the scheduling of the window (https://wicg.github.io/is-input-pending/#navigator-extension).</summary>
    /// <seealso href="https://wicg.github.io/is-input-pending/#dom-navigator-scheduling"/>
    [IdlMember("scheduling", "https://wicg.github.io/is-input-pending/#dom-navigator-scheduling")]
    public Scheduling Scheduling => _scheduling ??= new Scheduling(_window);
}
