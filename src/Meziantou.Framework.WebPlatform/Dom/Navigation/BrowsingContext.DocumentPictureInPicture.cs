namespace Meziantou.Framework.WebPlatform.Dom;

// The Document Picture-in-Picture windows (https://wicg.github.io/document-picture-in-picture/)
public partial class BrowsingContext
{
    /// <summary>
    /// Gets whether the browsing context is a Document Picture-in-Picture window (https://wicg.github.io/document-picture-in-picture/#is-document-picture-in-picture),
    /// opened by <c>documentPictureInPicture.requestWindow()</c>: an always-on-top top-level browsing context whose opener is
    /// the window that opened it.
    /// </summary>
    public bool IsDocumentPictureInPicture { get; private set; }

    /// <summary>The Picture-in-Picture window opened by the documents of this top-level browsing context, or null.</summary>
    internal BrowsingContext? DocumentPictureInPictureWindow { get; private set; }

    /// <summary>The size of the last Picture-in-Picture window opened by this top-level browsing context when it was closed (or now, if it is open), or null.</summary>
    internal PictureInPictureWindowSize? LastPictureInPictureWindowSize => DocumentPictureInPictureWindow is { } window ? window._pictureInPictureWindowSize : _lastClosedPictureInPictureWindowSize;

    private PictureInPictureWindowSize? _lastClosedPictureInPictureWindowSize;

    /// <summary>The size of the viewport of this Picture-in-Picture window.</summary>
    private PictureInPictureWindowSize _pictureInPictureWindowSize;

    /// <summary>The handle with which the user agent UI backend resizes and closes this Picture-in-Picture window.</summary>
    private PictureInPictureWindowHandle? _pictureInPictureHandle;

    /// <summary>The top-level browsing context that opened this Picture-in-Picture window (kept when the page disowns its opener).</summary>
    internal BrowsingContext? PictureInPictureOpener { get; private set; }

    /// <summary>
    /// https://wicg.github.io/document-picture-in-picture/#dom-documentpictureinpicture-requestwindow, steps 10 to 16: creates
    /// a new top-level traversable whose opener is <paramref name="opener"/>, with an initial about:blank document that has
    /// the mode of the opener (and its base URL, the about base URL of the initial document), sized to
    /// <paramref name="size"/>. It takes the system focus, as a new window.
    /// </summary>
    internal BrowsingContext CreateDocumentPictureInPictureWindow(Document opener, PictureInPictureWindowSize size, PictureInPictureWindowHandle handle)
    {
        var popupSandboxingFlags = (opener.ActiveSandboxingFlags & SandboxingFlags.PropagatesToAuxiliaryBrowsingContexts) != SandboxingFlags.None ? opener.ActiveSandboxingFlags : SandboxingFlags.None;
        var pip = CreateAuxiliaryBrowsingContext("", opener, popupSandboxingFlags, creator: this);
        pip.IsDocumentPictureInPicture = true;
        pip.IsPopup = true;
        pip._pictureInPictureHandle = handle;
        pip.PictureInPictureOpener = this;
        pip._pictureInPictureWindowSize = size;
        pip.ClientWindowViewportWidth = size.Width;
        pip.ClientWindowViewportHeight = size.Height;
        pip.Document.Mode = opener.Mode;
        DocumentPictureInPictureWindow = pip;
        handle.Attach(pip.SetPictureInPictureWindowSize, () => DocumentPictureInPictureModel.Close(pip));

        // The window of the new top-level browsing context is the active window of the user
        pip._hasSystemFocus = false;
        pip.QueueTask(() =>
        {
            if (pip.GetGroupTopLevelBrowsingContexts().Any(static context => context._hasSystemFocus))
            {
                pip.SetSystemFocus(true);
            }
        });

        return pip;
    }

    /// <summary>
    /// Resizes the viewport of this Picture-in-Picture window: the media queries are evaluated again, and the <c>resize</c>
    /// event is fired at its window by the next rendering update.
    /// </summary>
    internal void SetPictureInPictureWindowSize(PictureInPictureWindowSize size)
    {
        if (!IsDocumentPictureInPicture || IsDestroyed || size == _pictureInPictureWindowSize)
            return;

        _pictureInPictureWindowSize = size;
        ClientWindowViewportWidth = size.Width;
        ClientWindowViewportHeight = size.Height;
        foreach (var navigable in GetInclusiveDescendantNavigables())
        {
            navigable.Document.InvalidateLayout();
        }

        EventLoop.RequestRenderingOpportunity(Document);
    }

    /// <summary>This Picture-in-Picture window is destroyed: its opener remembers its size, and the backend is told it is closed.</summary>
    private void OnDocumentPictureInPictureWindowDestroyed()
    {
        if (!IsDocumentPictureInPicture)
            return;

        _pictureInPictureHandle?.MarkClosed();
        if (PictureInPictureOpener is { } opener && ReferenceEquals(opener.DocumentPictureInPictureWindow, this))
        {
            opener._lastClosedPictureInPictureWindowSize = _pictureInPictureWindowSize;
            opener.DocumentPictureInPictureWindow = null;
        }
    }
}
