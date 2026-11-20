using Meziantou.Framework.WebPlatform.Css.Cascade;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The options of <see cref="DocumentPictureInPicture.RequestWindow"/>.</summary>
/// <seealso href="https://wicg.github.io/document-picture-in-picture/#dictdef-documentpictureinpictureoptions"/>
[IdlInterface("DocumentPictureInPictureOptions", "https://wicg.github.io/document-picture-in-picture/#dictdef-documentpictureinpictureoptions")]
public sealed class DocumentPictureInPictureOptions
{
    /// <summary>Gets the requested width of the viewport of the window in CSS pixels, or 0 for the default width.</summary>
    /// <seealso href="https://wicg.github.io/document-picture-in-picture/#dom-documentpictureinpictureoptions-width"/>
    [IdlMember("width", "https://wicg.github.io/document-picture-in-picture/#dom-documentpictureinpictureoptions-width")]
    public ulong Width { get; init; }

    /// <summary>Gets the requested height of the viewport of the window in CSS pixels, or 0 for the default height.</summary>
    /// <seealso href="https://wicg.github.io/document-picture-in-picture/#dom-documentpictureinpictureoptions-height"/>
    [IdlMember("height", "https://wicg.github.io/document-picture-in-picture/#dom-documentpictureinpictureoptions-height")]
    public ulong Height { get; init; }

    /// <summary>Gets whether the user agent should not show a button that returns to the opener.</summary>
    /// <seealso href="https://wicg.github.io/document-picture-in-picture/#dom-documentpictureinpictureoptions-disallowreturntoopener"/>
    [IdlMember("disallowReturnToOpener", "https://wicg.github.io/document-picture-in-picture/#dom-documentpictureinpictureoptions-disallowreturntoopener")]
    public bool DisallowReturnToOpener { get; init; }

    /// <summary>Gets whether the window opens at its initial size rather than the size of the previous window of the opener.</summary>
    /// <seealso href="https://wicg.github.io/document-picture-in-picture/#dom-documentpictureinpictureoptions-preferinitialwindowplacement"/>
    [IdlMember("preferInitialWindowPlacement", "https://wicg.github.io/document-picture-in-picture/#dom-documentpictureinpictureoptions-preferinitialwindowplacement")]
    public bool PreferInitialWindowPlacement { get; init; }
}

/// <summary>The initialization dictionary of <see cref="DocumentPictureInPictureEvent"/>.</summary>
/// <seealso href="https://wicg.github.io/document-picture-in-picture/#dictdef-documentpictureinpictureeventinit"/>
[IdlInterface("DocumentPictureInPictureEventInit", "https://wicg.github.io/document-picture-in-picture/#dictdef-documentpictureinpictureeventinit")]
public sealed class DocumentPictureInPictureEventInit : EventInit
{
    /// <summary>Gets the Picture-in-Picture window.</summary>
    /// <seealso href="https://wicg.github.io/document-picture-in-picture/#dom-documentpictureinpictureeventinit-window"/>
    [IdlMember("window", "https://wicg.github.io/document-picture-in-picture/#dom-documentpictureinpictureeventinit-window")]
    public required Window Window { get; init; }
}

/// <summary>The <c>enter</c> event fired at <see cref="DocumentPictureInPicture"/> when a Picture-in-Picture window opens.</summary>
/// <seealso href="https://wicg.github.io/document-picture-in-picture/#documentpictureinpictureevent"/>
[IdlInterface("DocumentPictureInPictureEvent", "https://wicg.github.io/document-picture-in-picture/#documentpictureinpictureevent")]
public sealed class DocumentPictureInPictureEvent : Event
{
    /// <summary>Creates a document Picture-in-Picture event with the given type and initialization dictionary.</summary>
    /// <seealso href="https://wicg.github.io/document-picture-in-picture/#dom-documentpictureinpictureevent-documentpictureinpictureevent"/>
    [IdlMember("constructor", "https://wicg.github.io/document-picture-in-picture/#dom-documentpictureinpictureevent-documentpictureinpictureevent")]
    public DocumentPictureInPictureEvent(string type, DocumentPictureInPictureEventInit eventInitDict)
        : base(type, eventInitDict)
    {
        Window = eventInitDict.Window;
    }

    /// <summary>Gets the Picture-in-Picture window.</summary>
    /// <seealso href="https://wicg.github.io/document-picture-in-picture/#dom-documentpictureinpictureevent-window"/>
    [IdlMember("window", "https://wicg.github.io/document-picture-in-picture/#dom-documentpictureinpictureevent-window")]
    public Window Window { get; }
}

/// <summary>
/// The Document Picture-in-Picture API of a window (https://wicg.github.io/document-picture-in-picture/#documentpictureinpicture):
/// <see cref="RequestWindow"/> opens an always-on-top window whose document the page fills with any content.
/// </summary>
/// <seealso href="https://wicg.github.io/document-picture-in-picture/#documentpictureinpicture"/>
[IdlInterface("DocumentPictureInPicture", "https://wicg.github.io/document-picture-in-picture/#documentpictureinpicture")]
public sealed class DocumentPictureInPicture : EventTarget, IRealmBoundPlatformObject
{
    private readonly Window _window;

    /// <summary>https://wicg.github.io/document-picture-in-picture/#last-opened-window</summary>
    private Window? _lastOpenedWindow;

    internal DocumentPictureInPicture(Window window)
    {
        _window = window;
    }

    internal override Window? RelevantGlobal => _window;

    Window? IRealmBoundPlatformObject.RealmWindow => _window;

    /// <summary>Gets the Picture-in-Picture window opened by the window, or null when it is closed.</summary>
    /// <seealso href="https://wicg.github.io/document-picture-in-picture/#dom-documentpictureinpicture-window"/>
    [IdlMember("window", "https://wicg.github.io/document-picture-in-picture/#dom-documentpictureinpicture-window")]
    public Window? Window => _lastOpenedWindow is { Closed: false } window ? window : null;

    /// <summary>Gets or sets the handler of the <c>enter</c> event, fired when a Picture-in-Picture window opens.</summary>
    /// <seealso href="https://wicg.github.io/document-picture-in-picture/#dom-documentpictureinpicture-onenter"/>
    [IdlMember("onenter", "https://wicg.github.io/document-picture-in-picture/#dom-documentpictureinpicture-onenter")]
    public Action<Event>? OnEnter
    {
        get => GetEventHandler("enter");
        set => SetEventHandler("enter", value);
    }

    /// <summary>
    /// Opens a Picture-in-Picture window (https://wicg.github.io/document-picture-in-picture/#dom-documentpictureinpicture-requestwindow):
    /// a new top-level browsing context whose opener is the window, with an about:blank document that has the mode and the
    /// base URL of the document of the window. The task is completed at once with the new window; <c>enter</c> is fired by a
    /// later task. The previous Picture-in-Picture window of the window is closed. The task fails with a
    /// <c>NotAllowedError</c> <see cref="DomException"/> when the window is not a top-level window, is itself a
    /// Picture-in-Picture window, has no transient activation (which is consumed), or the user agent cannot open the window
    /// (<see cref="UserAgentUIBackend.OpenDocumentPictureInPictureWindow"/>), and with a <see cref="RangeErrorException"/>
    /// when only one of the width and the height is given.
    /// </summary>
    /// <param name="options">The options, or null for the default options.</param>
    /// <seealso href="https://wicg.github.io/document-picture-in-picture/#dom-documentpictureinpicture-requestwindow"/>
    [IdlMember("requestWindow", "https://wicg.github.io/document-picture-in-picture/#dom-documentpictureinpicture-requestwindow")]
    public Task<Window> RequestWindow(DocumentPictureInPictureOptions? options = null)
    {
        options ??= new DocumentPictureInPictureOptions();
        var window = _window;
        var document = window.Document;
        if (!document.IsFullyActive || window.BrowsingContext is not { } navigable || !ReferenceEquals(navigable.Document, document))
            return Fail("The document is not fully active");

        if (navigable.Parent is not null)
            return Fail("A Picture-in-Picture window can only be opened by a top-level window");

        if (navigable.IsDocumentPictureInPicture)
            return Fail("A Picture-in-Picture window cannot open a Picture-in-Picture window");

        if (!window.HasTransientActivation)
            return Fail("requestWindow() requires a user activation");

        if (options.Width > 0 && options.Height == 0)
            return Task.FromException<Window>(new RangeErrorException("The height must be given with the width"));

        if (options.Height > 0 && options.Width == 0)
            return Task.FromException<Window>(new RangeErrorException("The width must be given with the height"));

        window.ConsumeUserActivation();

        // The previous Picture-in-Picture window of the window is closed: the next one opens at its size
        if (_lastOpenedWindow?.BrowsingContext is { IsDestroyed: false } previous)
        {
            DocumentPictureInPictureModel.Close(previous);
        }

        PictureInPictureWindowSize? requestedSize = options.Width > 0 ? new((int)Math.Min(options.Width, int.MaxValue), (int)Math.Min(options.Height, int.MaxValue)) : null;
        var defaultSize = DocumentPictureInPictureModel.GetDefaultSize(navigable, requestedSize, options.PreferInitialWindowPlacement);
        var handle = new PictureInPictureWindowHandle(window);
        var request = new PictureInPictureWindowRequest(window, video: null, requestedSize, defaultSize, handle);
        PictureInPictureWindowSize? size;
        try
        {
            size = window.Options.UserAgentUI is { } backend ? backend.OpenDocumentPictureInPictureWindow(request) : defaultSize;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            size = null;
        }

        if (size is null || !document.IsFullyActive || navigable.IsDestroyed)
        {
            handle.MarkClosed();
            return Fail("The user agent cannot open the Picture-in-Picture window");
        }

        var pipNavigable = navigable.CreateDocumentPictureInPictureWindow(document, DocumentPictureInPictureModel.Clamp(navigable, size.Value), handle);
        var pipWindow = pipNavigable.Window;
        _lastOpenedWindow = pipWindow;
        document.QueueGlobalTask(() => FireEvent(new DocumentPictureInPictureEvent("enter", new DocumentPictureInPictureEventInit { Window = pipWindow })));
        return Task.FromResult(pipWindow);

        Task<Window> Fail(string message) => Task.FromException<Window>(new DomException(message, "NotAllowedError") { RealmWindow = window });
    }
}

public partial class Window
{
    private DocumentPictureInPicture? _documentPictureInPicture;

    /// <summary>Gets the Document Picture-in-Picture API of the window (https://wicg.github.io/document-picture-in-picture/#dom-window-documentpictureinpicture).</summary>
    /// <seealso href="https://wicg.github.io/document-picture-in-picture/#dom-window-documentpictureinpicture"/>
    [IdlMember("documentPictureInPicture", "https://wicg.github.io/document-picture-in-picture/#dom-window-documentpictureinpicture")]
    public DocumentPictureInPicture DocumentPictureInPicture => _documentPictureInPicture ??= new DocumentPictureInPicture(this);

    /// <summary>The Document Picture-in-Picture API of the window, or null when it was never requested.</summary>
    internal DocumentPictureInPicture? DocumentPictureInPictureOrNull => _documentPictureInPicture;
}

/// <summary>
/// The Document Picture-in-Picture windows (https://wicg.github.io/document-picture-in-picture/): top-level browsing contexts
/// whose "is document picture-in-picture" flag is set, opened by <see cref="DocumentPictureInPicture.RequestWindow"/>.
/// Nothing is displayed: the size of the window is the size of its viewport (there is no decoration), at most 80% of the
/// screen, and the user agent UI backend resizes and closes it through its <see cref="PictureInPictureWindowHandle"/>.
/// </summary>
internal static class DocumentPictureInPictureModel
{
    private const int DefaultWidth = 400;
    private const int DefaultHeight = 300;

    /// <summary>The smallest width and height of the viewport of a Picture-in-Picture window, in CSS pixels.</summary>
    private const int MinimumSize = 100;

    /// <summary>
    /// The size of a new window: the requested size, the size of the previous window of the opener (unless
    /// <paramref name="preferInitialWindowPlacement"/>), or the default size, clamped to the screen.
    /// </summary>
    public static PictureInPictureWindowSize GetDefaultSize(BrowsingContext opener, PictureInPictureWindowSize? requestedSize, bool preferInitialWindowPlacement)
    {
        var size = requestedSize ?? (preferInitialWindowPlacement ? null : opener.LastPictureInPictureWindowSize) ?? new PictureInPictureWindowSize(DefaultWidth, DefaultHeight);
        return Clamp(opener, size);
    }

    /// <summary>
    /// https://wicg.github.io/document-picture-in-picture/#dom-documentpictureinpicture-requestwindow, steps 13 and 14 ("clamp
    /// or ignore" a size that is too large or too small): the window covers at most 80% of the screen.
    /// </summary>
    public static PictureInPictureWindowSize Clamp(BrowsingContext navigable, PictureInPictureWindowSize size)
    {
        var screen = StyleEngine.CreateEnvironment(navigable.Top.Document);
        var maximumWidth = Math.Max(MinimumSize, (int)(screen.DeviceWidth * 0.8));
        var maximumHeight = Math.Max(MinimumSize, (int)(screen.DeviceHeight * 0.8));
        return new PictureInPictureWindowSize(Math.Clamp(size.Width, MinimumSize, maximumWidth), Math.Clamp(size.Height, MinimumSize, maximumHeight));
    }

    /// <summary>
    /// <c>resizeTo()</c> and <c>resizeBy()</c> of a Picture-in-Picture window (https://wicg.github.io/document-picture-in-picture/#resizing-the-pip-window):
    /// they need a transient activation, which they consume.
    /// </summary>
    /// <exception cref="DomException">A <c>NotAllowedError</c>: the window has no transient activation.</exception>
    public static void Resize(Window window, Func<PictureInPictureWindowSize, PictureInPictureWindowSize> resize)
    {
        // The methods do nothing in the other windows
        if (window.BrowsingContext is not { Parent: null, IsDocumentPictureInPicture: true, IsDestroyed: false } navigable)
            return;

        if (!window.HasTransientActivation)
            throw new DomException("Resizing a Picture-in-Picture window requires a user activation", "NotAllowedError") { RealmWindow = window };

        window.ConsumeUserActivation();
        var current = new PictureInPictureWindowSize(window.OuterWidth, window.OuterHeight);
        navigable.SetPictureInPictureWindowSize(Clamp(navigable, resize(current)));
    }

    /// <summary>Closes a Picture-in-Picture window, as <c>window.close()</c> does (from a task; the window is closed at once).</summary>
    public static void Close(BrowsingContext navigable)
    {
        if (navigable.IsDestroyed || navigable.IsClosing)
            return;

        navigable.IsClosing = true;
        navigable.QueueTask(navigable.Close);
    }

    /// <summary>
    /// https://wicg.github.io/document-picture-in-picture/#close-on-navigation: a navigation of a top-level window to another
    /// document closes its Picture-in-Picture window, and a Picture-in-Picture window that navigates to another document is
    /// closed instead. The windows are destroyed by a task, after their documents are unloaded: <c>window.closed</c> is
    /// false in their <c>pagehide</c> event handlers.
    /// </summary>
    /// <returns>True when the navigable is a Picture-in-Picture window: it is closed, and the navigation is aborted.</returns>
    public static bool OnNavigate(BrowsingContext navigable)
    {
        if (navigable.Parent is not null)
            return false;

        if (navigable.IsDocumentPictureInPicture)
        {
            navigable.QueueTask(navigable.Destroy);
            return true;
        }

        if (navigable.DocumentPictureInPictureWindow is { IsDestroyed: false } pipNavigable)
        {
            pipNavigable.QueueTask(pipNavigable.Destroy);
        }

        return false;
    }

    /// <summary>https://wicg.github.io/document-picture-in-picture/#close-on-destroy: the Picture-in-Picture window of a destroyed top-level window is closed.</summary>
    public static void OnDestroyed(BrowsingContext navigable)
    {
        if (navigable.DocumentPictureInPictureWindow is { IsDestroyed: false } pipNavigable)
        {
            pipNavigable.Destroy();
        }
    }
}
