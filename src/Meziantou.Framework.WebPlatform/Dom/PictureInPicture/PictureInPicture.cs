using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The floating window that shows a video in Picture-in-Picture (https://w3c.github.io/picture-in-picture/#pictureinpicturewindow):
/// its size while it is open, 0×0 once it is closed.
/// </summary>
/// <seealso href="https://w3c.github.io/picture-in-picture/#pictureinpicturewindow"/>
[IdlInterface("PictureInPictureWindow", "https://w3c.github.io/picture-in-picture/#pictureinpicturewindow")]
public sealed class PictureInPictureWindow : EventTarget, IRealmBoundPlatformObject
{
    private readonly Window _window;
    private PictureInPictureWindowSize _size;
    private bool _isOpened;

    internal PictureInPictureWindow(Window window, PictureInPictureWindowHandle handle)
    {
        _window = window;
        Handle = handle;
    }

    internal PictureInPictureWindowHandle Handle { get; }

    internal override Window? RelevantGlobal => _window;

    Window? IRealmBoundPlatformObject.RealmWindow => _window;

    /// <summary>Gets the width of the window in CSS pixels, or 0 when it is closed.</summary>
    /// <seealso href="https://w3c.github.io/picture-in-picture/#dom-pictureinpicturewindow-width"/>
    [IdlMember("width", "https://w3c.github.io/picture-in-picture/#dom-pictureinpicturewindow-width")]
    public int Width => _isOpened ? _size.Width : 0;

    /// <summary>Gets the height of the window in CSS pixels, or 0 when it is closed.</summary>
    /// <seealso href="https://w3c.github.io/picture-in-picture/#dom-pictureinpicturewindow-height"/>
    [IdlMember("height", "https://w3c.github.io/picture-in-picture/#dom-pictureinpicturewindow-height")]
    public int Height => _isOpened ? _size.Height : 0;

    /// <summary>Gets or sets the handler of the <c>resize</c> event, fired when the size of the window changes.</summary>
    /// <seealso href="https://w3c.github.io/picture-in-picture/#dom-pictureinpicturewindow-onresize"/>
    [IdlMember("onresize", "https://w3c.github.io/picture-in-picture/#dom-pictureinpicturewindow-onresize")]
    public Action<Event>? OnResize
    {
        get => GetEventHandler("resize");
        set => SetEventHandler("resize", value);
    }

    internal void Open(PictureInPictureWindowSize size)
    {
        _size = size;
        _isOpened = true;
    }

    /// <summary>The size of the window changed (the user resized it): <c>resize</c> is fired at the window.</summary>
    internal void SetSize(PictureInPictureWindowSize size)
    {
        if (!_isOpened || size == _size)
            return;

        _size = size;
        FireEvent(new Event("resize"));
    }

    /// <summary>https://w3c.github.io/picture-in-picture/#close-window-algorithm: the state of the window is closed.</summary>
    internal void Close()
    {
        _isOpened = false;
        Handle.MarkClosed();
    }
}

/// <summary>The initialization dictionary of <see cref="PictureInPictureEvent"/>.</summary>
/// <seealso href="https://w3c.github.io/picture-in-picture/#dictdef-pictureinpictureeventinit"/>
[IdlInterface("PictureInPictureEventInit", "https://w3c.github.io/picture-in-picture/#dictdef-pictureinpictureeventinit")]
public sealed class PictureInPictureEventInit : EventInit
{
    /// <summary>Gets the Picture-in-Picture window of the event.</summary>
    /// <seealso href="https://w3c.github.io/picture-in-picture/#dom-pictureinpictureeventinit-pictureinpicturewindow"/>
    [IdlMember("pictureInPictureWindow", "https://w3c.github.io/picture-in-picture/#dom-pictureinpictureeventinit-pictureinpicturewindow")]
    public required PictureInPictureWindow PictureInPictureWindow { get; init; }
}

/// <summary>
/// The event fired at a video when it enters (<c>enterpictureinpicture</c>) or leaves (<c>leavepictureinpicture</c>)
/// Picture-in-Picture (https://w3c.github.io/picture-in-picture/#pictureinpictureevent).
/// </summary>
/// <seealso href="https://w3c.github.io/picture-in-picture/#pictureinpictureevent"/>
[IdlInterface("PictureInPictureEvent", "https://w3c.github.io/picture-in-picture/#pictureinpictureevent")]
public sealed class PictureInPictureEvent : Event
{
    /// <summary>Creates a Picture-in-Picture event with the given type and initialization dictionary.</summary>
    /// <seealso href="https://w3c.github.io/picture-in-picture/#dom-pictureinpictureevent-pictureinpictureevent"/>
    [IdlMember("constructor", "https://w3c.github.io/picture-in-picture/#dom-pictureinpictureevent-pictureinpictureevent")]
    public PictureInPictureEvent(string type, PictureInPictureEventInit eventInitDict)
        : base(type, eventInitDict)
    {
        PictureInPictureWindow = eventInitDict.PictureInPictureWindow;
    }

    /// <summary>Gets the Picture-in-Picture window of the video.</summary>
    /// <seealso href="https://w3c.github.io/picture-in-picture/#dom-pictureinpictureevent-pictureinpicturewindow"/>
    [IdlMember("pictureInPictureWindow", "https://w3c.github.io/picture-in-picture/#dom-pictureinpictureevent-pictureinpicturewindow")]
    public PictureInPictureWindow PictureInPictureWindow { get; }
}

public partial class HtmlVideoElement
{
    /// <summary>The promises returned by <see cref="RequestPictureInPicture"/> that are not settled yet.</summary>
    internal List<TaskCompletionSource<PictureInPictureWindow>>? PendingPictureInPictureRequests { get; set; }

    /// <summary>Whether the video has a video track: its media resource has video data.</summary>
    internal bool HasVideoTrack => MediaVideoWidth > 0 && MediaVideoHeight > 0;

    /// <summary>Whether the element is the Picture-in-Picture element of its node document.</summary>
    internal bool IsPictureInPictureElement => ReferenceEquals(NodeDocument.PictureInPictureElementInternal, this);

    /// <summary>
    /// Shows the video in a floating window (https://w3c.github.io/picture-in-picture/#request-pip). The task completes with the
    /// window once <c>enterpictureinpicture</c> is fired at the element. It fails with a <c>SecurityError</c>
    /// <see cref="DomException"/> when the permissions policy of the document does not allow the <c>picture-in-picture</c>
    /// feature, an <c>InvalidStateError</c> when the video has no metadata or no video track, its
    /// <see cref="DisablePictureInPicture"/> attribute is set, or the user agent cannot open the window, and a
    /// <c>NotAllowedError</c> when no element of the document is in Picture-in-Picture and the window has no transient
    /// activation (which is consumed). The window is opened by <see cref="UserAgentUIBackend.OpenPictureInPictureWindowAsync"/>.
    /// </summary>
    /// <seealso href="https://w3c.github.io/picture-in-picture/#dom-htmlvideoelement-requestpictureinpicture"/>
    [IdlMember("requestPictureInPicture", "https://w3c.github.io/picture-in-picture/#dom-htmlvideoelement-requestpictureinpicture")]
    public Task<PictureInPictureWindow> RequestPictureInPicture() => PictureInPictureModel.Request(this);

    /// <summary>
    /// Gets or sets whether the user agent must not show the video in Picture-in-Picture (https://w3c.github.io/picture-in-picture/#disable-pip):
    /// when set, the pending requests fail and the video leaves Picture-in-Picture.
    /// </summary>
    /// <seealso href="https://w3c.github.io/picture-in-picture/#dom-htmlvideoelement-disablepictureinpicture"/>
    [IdlMember("disablePictureInPicture", "https://w3c.github.io/picture-in-picture/#dom-htmlvideoelement-disablepictureinpicture")]
    public bool DisablePictureInPicture
    {
        get => Reflection.GetBoolean(this, "disablepictureinpicture");
        set => Reflection.SetBoolean(this, "disablepictureinpicture", value);
    }

    /// <summary>Gets or sets the handler of the <c>enterpictureinpicture</c> event.</summary>
    /// <seealso href="https://w3c.github.io/picture-in-picture/#dom-htmlvideoelement-onenterpictureinpicture"/>
    [IdlMember("onenterpictureinpicture", "https://w3c.github.io/picture-in-picture/#dom-htmlvideoelement-onenterpictureinpicture")]
    public Action<Event>? OnEnterPictureInPicture
    {
        get => GetEventHandler("enterpictureinpicture");
        set => SetEventHandler("enterpictureinpicture", value);
    }

    /// <summary>Gets or sets the handler of the <c>leavepictureinpicture</c> event.</summary>
    /// <seealso href="https://w3c.github.io/picture-in-picture/#dom-htmlvideoelement-onleavepictureinpicture"/>
    [IdlMember("onleavepictureinpicture", "https://w3c.github.io/picture-in-picture/#dom-htmlvideoelement-onleavepictureinpicture")]
    public Action<Event>? OnLeavePictureInPicture
    {
        get => GetEventHandler("leavepictureinpicture");
        set => SetEventHandler("leavepictureinpicture", value);
    }
}

public partial class Document
{
    /// <summary>https://w3c.github.io/picture-in-picture/#picture-in-picture-element</summary>
    internal HtmlVideoElement? PictureInPictureElementInternal { get; private set; }

    /// <summary>The window of <see cref="PictureInPictureElementInternal"/>.</summary>
    internal PictureInPictureWindow? PictureInPictureWindowInternal { get; private set; }

    internal void SetPictureInPictureElement(HtmlVideoElement? element, PictureInPictureWindow? window)
    {
        PictureInPictureElementInternal = element;
        PictureInPictureWindowInternal = window;

        // The :picture-in-picture pseudo-class
        IncrementElementStateVersion();
    }

    /// <summary>
    /// Gets whether the document can show videos in Picture-in-Picture (https://w3c.github.io/picture-in-picture/#dom-document-pictureinpictureenabled):
    /// its permissions policy allows the <c>picture-in-picture</c> feature.
    /// </summary>
    /// <seealso href="https://w3c.github.io/picture-in-picture/#dom-document-pictureinpictureenabled"/>
    [IdlMember("pictureInPictureEnabled", "https://w3c.github.io/picture-in-picture/#dom-document-pictureinpictureenabled")]
    public bool PictureInPictureEnabled => PermissionsPolicyAlgorithms.IsAllowedToUse(this, PolicyControlledFeature.PictureInPicture, report: false);

    /// <summary>
    /// Gets the video of the document shown in Picture-in-Picture (https://w3c.github.io/picture-in-picture/#dom-documentorshadowroot-pictureinpictureelement),
    /// retargeted against the document: the host of the shadow tree of the video when it is in a shadow tree.
    /// </summary>
    /// <seealso href="https://w3c.github.io/picture-in-picture/#dom-documentorshadowroot-pictureinpictureelement"/>
    [IdlMember("pictureInPictureElement", "https://w3c.github.io/picture-in-picture/#dom-documentorshadowroot-pictureinpictureelement")]
    public Element? PictureInPictureElement => PictureInPictureElementInternal is { } element ? EventDispatcher.Retarget(element, this) as Element : null;

    /// <summary>
    /// Stops showing the video of the document in Picture-in-Picture (https://w3c.github.io/picture-in-picture/#exit-pip):
    /// <c>leavepictureinpicture</c> is fired at the video. The task fails with an <c>InvalidStateError</c>
    /// <see cref="DomException"/> when no video of the document is in Picture-in-Picture.
    /// </summary>
    /// <seealso href="https://w3c.github.io/picture-in-picture/#dom-document-exitpictureinpicture"/>
    [IdlMember("exitPictureInPicture", "https://w3c.github.io/picture-in-picture/#dom-document-exitpictureinpicture")]
    public Task ExitPictureInPicture() => PictureInPictureModel.Exit(this);
}

public partial class ShadowRoot
{
    /// <summary>
    /// Gets the video shown in Picture-in-Picture retargeted against the shadow root
    /// (https://w3c.github.io/picture-in-picture/#dom-documentorshadowroot-pictureinpictureelement), when it is in the shadow
    /// tree and the host is connected; otherwise null.
    /// </summary>
    /// <seealso href="https://w3c.github.io/picture-in-picture/#dom-documentorshadowroot-pictureinpictureelement"/>
    [IdlMember("pictureInPictureElement", "https://w3c.github.io/picture-in-picture/#dom-documentorshadowroot-pictureinpictureelement")]
    public Element? PictureInPictureElement
    {
        get
        {
            if (!Host.IsConnected || NodeDocument.PictureInPictureElementInternal is not { } element)
                return null;

            var candidate = EventDispatcher.Retarget(element, this) as Element;
            return candidate is not null && ReferenceEquals(candidate.GetRoot(), this) ? candidate : null;
        }
    }
}

/// <summary>
/// The Picture-in-Picture API (https://w3c.github.io/picture-in-picture/). The floating window is opened by the user agent UI
/// backend (<see cref="UserAgentUIBackend.OpenPictureInPictureWindowAsync"/>), which resizes and closes it through its
/// <see cref="PictureInPictureWindowHandle"/>. The steps of the parallel queue of the document are tasks of the event loop.
/// </summary>
internal static class PictureInPictureModel
{
    private const int DefaultWidth = 400;
    private const int DefaultHeight = 300;

    /// <summary>https://w3c.github.io/picture-in-picture/#request-pip</summary>
    public static Task<PictureInPictureWindow> Request(HtmlVideoElement video)
    {
        var document = video.NodeDocument;
        if (!document.IsFullyActive || document.RelevantWindow is not { } window || !ReferenceEquals(window.Document, document))
            return Task.FromException<PictureInPictureWindow>(new DomException("The document is not fully active", "InvalidStateError"));

        if (!PermissionsPolicyAlgorithms.IsAllowedToUse(document, PolicyControlledFeature.PictureInPicture))
            return Fail(window, "The permissions policy of the document does not allow picture-in-picture", "SecurityError");

        if (video.ReadyState == HtmlMediaElement.HaveNothing)
            return Fail(window, "The video has no metadata", "InvalidStateError");

        if (!video.HasVideoTrack)
            return Fail(window, "The video has no video track", "InvalidStateError");

        if (video.DisablePictureInPicture)
            return Fail(window, "The disablePictureInPicture attribute of the video is set", "InvalidStateError");

        if (document.PictureInPictureElementInternal is null)
        {
            if (!window.HasTransientActivation)
                return Fail(window, "requestPictureInPicture() requires a user activation", "NotAllowedError");

            window.ConsumeUserActivation();
        }

        if (video.IsPictureInPictureElement)
            return Task.FromResult(document.PictureInPictureWindowInternal!);

        var promise = new TaskCompletionSource<PictureInPictureWindow>();
        (video.PendingPictureInPictureRequests ??= []).Add(promise);

        var handle = new PictureInPictureWindowHandle(window);
        var pipWindow = new PictureInPictureWindow(window, handle);
        var request = new PictureInPictureWindowRequest(window, video, requestedSize: null, GetDefaultSize(video), handle);
        OpenWindow(window, request.DefaultSize, (backend, cancellationToken) => backend.OpenPictureInPictureWindowAsync(request, cancellationToken), size =>
        {
            // The request was rejected meanwhile (the disablePictureInPicture attribute was set)
            if (video.PendingPictureInPictureRequests?.Remove(promise) is not true)
            {
                handle.MarkClosed();
                return;
            }

            if (size is null || !document.IsFullyActive)
            {
                handle.MarkClosed();
                promise.SetException(CreateException(window, "The user agent cannot open the Picture-in-Picture window", "InvalidStateError"));
                return;
            }

            // Another request of the video entered Picture-in-Picture meanwhile
            if (video.IsPictureInPictureElement)
            {
                handle.MarkClosed();
                promise.SetResult(document.PictureInPictureWindowInternal!);
                return;
            }

            // The previous video leaves Picture-in-Picture inline: its leavepictureinpicture event is fired before the
            // enterpictureinpicture event of the new one, in the same task
            if (document.PictureInPictureElementInternal is not null)
            {
                ExitCore(document, PictureInPictureEventMode.Synchronous);
            }

            pipWindow.Open(PictureInPictureWindowHandle.Clamp(size.Value));
            handle.Attach(pipWindow.SetSize, () =>
            {
                if (ReferenceEquals(document.PictureInPictureWindowInternal, pipWindow))
                {
                    ExitCore(document, PictureInPictureEventMode.Synchronous);
                }
            });
            document.SetPictureInPictureElement(video, pipWindow);
            if (ReferenceEquals(document.FullscreenElementInternal, video))
            {
                FullscreenModel.FullyExit(document);
            }

            video.FireEvent(new PictureInPictureEvent("enterpictureinpicture", new PictureInPictureEventInit { Bubbles = true, PictureInPictureWindow = pipWindow }));
            promise.SetResult(pipWindow);
        });

        return promise.Task;
    }

    /// <summary>https://w3c.github.io/picture-in-picture/#dom-document-exitpictureinpicture</summary>
    public static Task Exit(Document document)
    {
        if (!document.IsFullyActive || document.RelevantWindow is not { } window || !ReferenceEquals(window.Document, document))
            return Task.FromException(new DomException("The document is not fully active", "InvalidStateError"));

        if (document.PictureInPictureElementInternal is null)
            return Task.FromException(CreateException(window, "No video of the document is in Picture-in-Picture", "InvalidStateError"));

        var promise = new TaskCompletionSource();
        window.QueueGlobalTask(() =>
        {
            if (document.PictureInPictureElementInternal is null)
            {
                promise.SetException(CreateException(window, "No video of the document is in Picture-in-Picture", "InvalidStateError"));
                return;
            }

            ExitCore(document, PictureInPictureEventMode.Synchronous);
            promise.SetResult();
        });

        return promise.Task;
    }

    /// <summary>
    /// The <c>disablePictureInPicture</c> attribute is added to a video (https://w3c.github.io/picture-in-picture/#disable-pip):
    /// its pending requests fail, and it leaves Picture-in-Picture.
    /// </summary>
    public static void OnDisabled(HtmlVideoElement video)
    {
        if (video.PendingPictureInPictureRequests is { Count: > 0 } pending)
        {
            video.PendingPictureInPictureRequests = null;
            var window = video.NodeDocument.RelevantWindow;
            foreach (var promise in pending)
            {
                promise.TrySetException(CreateException(window, "The disablePictureInPicture attribute of the video is set", "InvalidStateError"));
            }
        }

        if (video.IsPictureInPictureElement)
        {
            ExitCore(video.NodeDocument, PictureInPictureEventMode.Queued);
        }
    }

    /// <summary>
    /// The unloading document cleanup steps (https://w3c.github.io/picture-in-picture/#exit-pip): the video of the document
    /// leaves Picture-in-Picture (no event is fired at the unloaded document).
    /// </summary>
    public static void OnDocumentUnloaded(Document document)
    {
        if (document.PictureInPictureElementInternal is not null)
        {
            ExitCore(document, PictureInPictureEventMode.None);
        }
    }

    /// <summary>https://w3c.github.io/picture-in-picture/#exit-pip: the Picture-in-Picture element is unset before the event is fired.</summary>
    private static void ExitCore(Document document, PictureInPictureEventMode eventMode)
    {
        var video = document.PictureInPictureElementInternal!;
        var pipWindow = document.PictureInPictureWindowInternal!;
        document.SetPictureInPictureElement(null, null);
        pipWindow.Close();
        switch (eventMode)
        {
            case PictureInPictureEventMode.Synchronous:
                FireLeaveEvent();
                break;

            case PictureInPictureEventMode.Queued:
                document.QueueGlobalTask(FireLeaveEvent);
                break;
        }

        void FireLeaveEvent() => video.FireEvent(new PictureInPictureEvent("leavepictureinpicture", new PictureInPictureEventInit { Bubbles = true, PictureInPictureWindow = pipWindow }));
    }

    /// <summary>The natural size of the video scaled to fit the default size of the window.</summary>
    private static PictureInPictureWindowSize GetDefaultSize(HtmlVideoElement video)
    {
        double width = video.VideoWidth;
        double height = video.VideoHeight;
        if (width <= 0 || height <= 0)
            return new PictureInPictureWindowSize(DefaultWidth, DefaultHeight);

        var scale = Math.Min(DefaultWidth / width, DefaultHeight / height);
        return new PictureInPictureWindowSize(Math.Max(1, (int)Math.Round(width * scale, MidpointRounding.AwayFromZero)), Math.Max(1, (int)Math.Round(height * scale, MidpointRounding.AwayFromZero)));
    }

    /// <summary>
    /// Opens a Picture-in-Picture window with the backend of the window (or at its default size without a backend), then calls
    /// <paramref name="complete"/> from a task of the event loop of the window with its size, or null when it cannot be opened.
    /// </summary>
    internal static void OpenWindow(Window window, PictureInPictureWindowSize defaultSize, Func<UserAgentUIBackend, CancellationToken, ValueTask<PictureInPictureWindowSize?>> open, Action<PictureInPictureWindowSize?> complete)
    {
        if (window.Options.UserAgentUI is null)
        {
            window.QueueGlobalTask(() => complete(defaultSize));
            return;
        }

        UserAgentUI.Show(window, open, dismissed: null, complete);
    }

    private static Task<PictureInPictureWindow> Fail(Window window, string message, string name) => Task.FromException<PictureInPictureWindow>(CreateException(window, message, name));

    private static DomException CreateException(Window? window, string message, string name) => new(message, name) { RealmWindow = window };

    private enum PictureInPictureEventMode
    {
        None,
        Synchronous,
        Queued,
    }
}
