namespace Meziantou.Framework.WebPlatform.Dom;

// The Picture-in-Picture windows (https://w3c.github.io/picture-in-picture/ and https://wicg.github.io/document-picture-in-picture/)
public abstract partial class UserAgentUIBackend
{
    /// <summary>
    /// Opens the Picture-in-Picture window of a video (<see cref="HtmlVideoElement.RequestPictureInPicture"/>): the backend
    /// shows the video in a floating window and returns the size of the window in CSS pixels, or null when the user agent
    /// cannot open it (the request fails with an <c>InvalidStateError</c>). The backend changes the size of the window and
    /// closes it later through <see cref="PictureInPictureWindowRequest.Handle"/>.
    /// </summary>
    /// <remarks>
    /// Unlike the prompts, opening a Picture-in-Picture window does not ask the user: the default implementation (and the
    /// user agent without a backend) opens the window at <see cref="PictureInPictureWindowRequest.DefaultSize"/>.
    /// </remarks>
    /// <param name="request">The window to open.</param>
    /// <param name="cancellationToken">Canceled when the page no longer needs the window (e.g. its document is unloaded).</param>
    /// <returns>The size of the window, or null when it cannot be opened.</returns>
    public virtual ValueTask<PictureInPictureWindowSize?> OpenPictureInPictureWindowAsync(PictureInPictureWindowRequest request, CancellationToken cancellationToken)
        => new(request.DefaultSize);

    /// <summary>
    /// Opens the Picture-in-Picture window of a document (<see cref="DocumentPictureInPicture.RequestWindow"/>), a floating
    /// top-level browsing context, and returns the size of its viewport in CSS pixels, or null when the user agent cannot open
    /// it (the request fails with a <c>NotAllowedError</c>). The method is synchronous: <c>requestWindow()</c> returns the new
    /// window at once. The size is clamped to 80% of the screen. The backend changes the size of the window and closes it
    /// later through <see cref="PictureInPictureWindowRequest.Handle"/>; the browsing context is
    /// <see cref="BrowsingContext.IsDocumentPictureInPicture"/>.
    /// </summary>
    /// <remarks>
    /// Unlike the prompts, opening a Picture-in-Picture window does not ask the user: the default implementation (and the
    /// user agent without a backend) opens the window at <see cref="PictureInPictureWindowRequest.DefaultSize"/>.
    /// </remarks>
    /// <param name="request">The window to open.</param>
    /// <returns>The size of the viewport of the window, or null when it cannot be opened.</returns>
    public virtual PictureInPictureWindowSize? OpenDocumentPictureInPictureWindow(PictureInPictureWindowRequest request)
        => request.DefaultSize;
}

/// <summary>The size of a Picture-in-Picture window, in CSS pixels.</summary>
/// <param name="Width">The width of the window.</param>
/// <param name="Height">The height of the window.</param>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly record struct PictureInPictureWindowSize(int Width, int Height);

/// <summary>
/// A Picture-in-Picture window to open, given to <see cref="UserAgentUIBackend.OpenPictureInPictureWindowAsync"/> (the window
/// of a video) or <see cref="UserAgentUIBackend.OpenDocumentPictureInPictureWindow"/> (the window of a document).
/// </summary>
public sealed class PictureInPictureWindowRequest : UserAgentPrompt
{
    internal PictureInPictureWindowRequest(Window window, HtmlVideoElement? video, PictureInPictureWindowSize? requestedSize, PictureInPictureWindowSize defaultSize, PictureInPictureWindowHandle handle)
        : base(window)
    {
        Video = video;
        RequestedSize = requestedSize;
        DefaultSize = defaultSize;
        Handle = handle;
    }

    /// <summary>Gets the video shown in the window, or null for the window of a document.</summary>
    public HtmlVideoElement? Video { get; }

    /// <summary>Gets the size of the viewport requested by the page (the <c>width</c> and <c>height</c> options of <c>requestWindow()</c>), or null.</summary>
    public PictureInPictureWindowSize? RequestedSize { get; }

    /// <summary>
    /// Gets the size the user agent gives the window by default: for a video, its natural size scaled to fit 400×300 CSS
    /// pixels; for a document, the requested size clamped to the screen, the size of the last window of the opener (unless
    /// the page prefers the initial placement), or 400×300 CSS pixels.
    /// </summary>
    public PictureInPictureWindowSize DefaultSize { get; }

    /// <summary>Gets the handle with which the backend changes the size of the window or closes it, once it is open.</summary>
    public PictureInPictureWindowHandle Handle { get; }
}

/// <summary>
/// The handle of an open Picture-in-Picture window, with which the user agent changes its size or closes it (e.g. the user
/// resizes or closes the floating window). Its methods can be called from any thread: the changes are applied by a task of
/// the event loop of the window that opened it.
/// </summary>
public sealed class PictureInPictureWindowHandle
{
    /// <summary>The largest size of a Picture-in-Picture window, in CSS pixels.</summary>
    internal const int MaximumSize = 16384;

    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Window _window;
    private Action<PictureInPictureWindowSize>? _resize;
    private Action? _close;
    private int _isClosed;

    internal PictureInPictureWindowHandle(Window window)
    {
        _window = window;
    }

    /// <summary>Gets a task that completes once the window is closed (by the page, the user agent or the backend).</summary>
    public Task Closed => _closed.Task;

    /// <summary>Gets whether the window is closed.</summary>
    public bool IsClosed => Volatile.Read(ref _isClosed) != 0;

    /// <summary>Changes the size of the window (e.g. the user resized it): the page is notified with a <c>resize</c> event. The size is clamped to [1, 16384] CSS pixels.</summary>
    /// <param name="size">The new size, in CSS pixels.</param>
    public void Resize(PictureInPictureWindowSize size)
    {
        var clamped = Clamp(size);
        _window.QueueGlobalTask(() =>
        {
            if (!IsClosed)
            {
                _resize?.Invoke(clamped);
            }
        });
    }

    /// <summary>Closes the window (e.g. the user closed it): the video leaves Picture-in-Picture, or the document window is closed.</summary>
    public void Close()
    {
        _window.QueueGlobalTask(() =>
        {
            if (!IsClosed)
            {
                _close?.Invoke();
            }
        });
    }

    internal static PictureInPictureWindowSize Clamp(PictureInPictureWindowSize size) => new(Math.Clamp(size.Width, 1, MaximumSize), Math.Clamp(size.Height, 1, MaximumSize));

    /// <summary>Attaches the steps of the window, run on the event loop of the opener.</summary>
    internal void Attach(Action<PictureInPictureWindowSize> resize, Action close)
    {
        _resize = resize;
        _close = close;
    }

    /// <summary>The window is closed: <see cref="Closed"/> completes, and the handle does nothing anymore.</summary>
    internal void MarkClosed()
    {
        if (Interlocked.Exchange(ref _isClosed, 1) != 0)
            return;

        _resize = null;
        _close = null;
        _closed.SetResult();
    }
}
