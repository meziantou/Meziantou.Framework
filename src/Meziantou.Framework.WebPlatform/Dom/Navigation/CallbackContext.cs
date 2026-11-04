namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The place where a callback of <see cref="BrowsingContextOptions"/> is raised: the first argument of every callback (e.g.
/// <see cref="BrowsingContextOptions.ConsoleMessage"/>, <see cref="BrowsingContextOptions.DocumentActivated"/>). It is a
/// snapshot taken when the callback is invoked: it does not change when the browsing context navigates later.
/// </summary>
/// <remarks>
/// The callbacks raised by a browsing context (its creation, its navigations, the fullscreen and pointer lock requests, the
/// downloads) have a <see cref="BrowsingContext"/>, and <see cref="Global"/> is its active window (null when it has none).
/// The callbacks raised by a global object (the console messages, the unhandled errors, the content security policy
/// violations, the dialogs, the file picker, the clipboard permission) have a <see cref="Global"/>: a window, a worker or a
/// worklet. The <see cref="BrowsingContext"/> of a worker, of a worklet, or of a window whose document has no browsing context,
/// is null.
/// </remarks>
public sealed class CallbackContext
{
    private CallbackContext(BrowsingContextOptions options, BrowsingContext? browsingContext, GlobalScope? global)
    {
        Options = options;
        BrowsingContext = browsingContext;
        Global = global;
    }

    /// <summary>Gets the options whose callback is invoked.</summary>
    public BrowsingContextOptions Options { get; }

    /// <summary>
    /// Gets the browsing context of the callback: the browsing context that raised it, or the browsing context of the window
    /// that raised it. It is null for a worker, a worklet, or a window whose document has no browsing context.
    /// </summary>
    public BrowsingContext? BrowsingContext { get; }

    /// <summary>
    /// Gets the global object of the callback: the window, the worker or the worklet that raised it, or the active window of the
    /// browsing context that raised it (null when it has none).
    /// </summary>
    public GlobalScope? Global { get; }

    /// <summary>Gets the window of the callback (<see cref="Global"/> when it is a window), or null (e.g. a worker or a worklet).</summary>
    public Window? Window => Global as Window;

    /// <summary>Gets the document of <see cref="Window"/>, or null when the callback has no window.</summary>
    public Document? Document => Window?.Document;

    /// <summary>Creates the context of a callback raised by a global object (a window, a worker or a worklet).</summary>
    internal static CallbackContext For(GlobalScope global) => new(global.Options, (global as Window)?.BrowsingContext, global);

    /// <summary>Creates the context of a callback raised by a browsing context.</summary>
    internal static CallbackContext For(BrowsingContext browsingContext) => new(browsingContext.Options, browsingContext, browsingContext.Document.RelevantWindow);
}
