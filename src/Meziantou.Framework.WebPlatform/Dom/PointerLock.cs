namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The options of <see cref="Element.RequestPointerLock"/> (https://w3c.github.io/pointerlock/#pointerlockoptions-dictionary).</summary>
[IdlInterface("PointerLockOptions", "https://w3c.github.io/pointerlock/#pointerlockoptions-dictionary")]
public sealed class PointerLockOptions
{
    /// <summary>Gets whether the movements of the pointer must not be adjusted by the platform (e.g. by the acceleration of the mouse).</summary>
    [IdlMember("unadjustedMovement", "https://w3c.github.io/pointerlock/#dom-pointerlockoptions-unadjustedmovement")]
    public bool UnadjustedMovement { get; init; }
}

public partial class Element
{
    /// <summary>
    /// Locks the mouse of the user to the element (https://w3c.github.io/pointerlock/#dom-element-requestpointerlock): its
    /// events are then fired at the element, with the position of the pointer when the lock was entered and the movements
    /// of the device (<see cref="MouseEvent.MovementX"/>, <see cref="MouseEvent.MovementY"/>), until the lock is exited.
    /// The window must have transient activation, unless the document released a pointer lock with
    /// <see cref="Document.ExitPointerLock"/>. The pointer is locked by a task, which fires <c>pointerlockchange</c> at the
    /// document; the task fails with a <see cref="DomException"/> when the pointer cannot be locked (<c>pointerlockerror</c>
    /// is fired).
    /// </summary>
    /// <param name="options">The options, or null for the default options.</param>
    [IdlMember("requestPointerLock", "https://w3c.github.io/pointerlock/#dom-element-requestpointerlock")]
    public Task RequestPointerLock(PointerLockOptions? options = null) => PointerLockModel.Request(this, options ?? new PointerLockOptions());
}

public partial class Document
{
    /// <summary>
    /// Whether the document "has previously released a successful pointer lock with exitPointerLock()"
    /// (https://w3c.github.io/pointerlock/#dom-element-requestpointerlock): it can then lock the pointer again without user
    /// activation, until the user exits a pointer lock of the document (e.g. with the Escape key).
    /// </summary>
    internal bool HasReleasedPointerLock { get; set; }

    /// <summary>
    /// Gets the element the pointer is locked to when it is in the document (https://w3c.github.io/pointerlock/#dom-documentorshadowroot-pointerlockelement):
    /// the host of the shadow tree of the element when it is in a shadow tree. Null while a request is pending.
    /// </summary>
    [IdlMember("pointerLockElement", "https://w3c.github.io/pointerlock/#dom-documentorshadowroot-pointerlockelement")]
    public Element? PointerLockElement => PointerLockModel.GetPointerLockElement(this, this);

    /// <summary>
    /// Exits the pointer lock when the pointer is locked to an element of the document
    /// (https://w3c.github.io/pointerlock/#dom-document-exitpointerlock); <c>pointerlockchange</c> is fired by a task.
    /// </summary>
    [IdlMember("exitPointerLock", "https://w3c.github.io/pointerlock/#dom-document-exitpointerlock")]
    public void ExitPointerLock() => PointerLockModel.Exit(this);
}

public partial class ShadowRoot
{
    /// <summary>
    /// Gets the element the pointer is locked to when it is in the shadow tree (https://w3c.github.io/pointerlock/#dom-documentorshadowroot-pointerlockelement):
    /// the pointer-lock target retargeted against the shadow root.
    /// </summary>
    [IdlMember("pointerLockElement", "https://w3c.github.io/pointerlock/#dom-documentorshadowroot-pointerlockelement")]
    public Element? PointerLockElement => Host.IsConnected ? PointerLockModel.GetPointerLockElement(NodeDocument, this) : null;
}

public sealed partial class BrowsingContext
{
    /// <summary>
    /// The pointer-lock target of the user agent (https://w3c.github.io/pointerlock/#dfn-pointer-lock-target), of the
    /// top-level browsing context: the element of one of its documents the mouse is locked to, or null.
    /// </summary>
    internal Element? PointerLockTarget { get; set; }

    /// <summary>https://w3c.github.io/pointerlock/#dfn-pointer-lock-options</summary>
    internal PointerLockOptions? PointerLockOptions { get; set; }

    /// <summary>
    /// Exits the pointer lock of the top-level browsing context, as the default unlock gesture of the user does
    /// (https://w3c.github.io/pointerlock/#dfn-default-unlock-gesture): a host calls it when its own gesture ends the lock
    /// or when the system takes the pointer back. <c>pointerlockchange</c> is fired by a task at the document of the
    /// pointer-lock target, which needs a user activation to lock the pointer again. The Escape key (a trusted
    /// <c>keydown</c> event) and the loss of the system focus (<see cref="HasSystemFocus"/>) or of the visibility
    /// (<see cref="IsVisible"/>) exit the pointer lock too.
    /// </summary>
    public void ExitPointerLock() => PointerLockModel.ExitByUserAgent(Top);
}

/// <summary>
/// The Pointer Lock API (https://w3c.github.io/pointerlock/). The pointer-lock state belongs to the top-level browsing
/// context (the mouse of <see cref="BrowsingContext.Input"/>): the host is asked to lock its pointer
/// (<see cref="BrowsingContextOptions.RequestPointerLock"/>, accepted by default: a headless host has no cursor) and told
/// when the lock ends (<see cref="BrowsingContextOptions.ExitPointerLock"/>). The steps of the lock requests queue, which
/// the specification runs in parallel, are tasks of the event loop.
/// </summary>
internal static class PointerLockModel
{
    /// <summary>https://w3c.github.io/pointerlock/#dom-documentorshadowroot-pointerlockelement</summary>
    public static Element? GetPointerLockElement(Document document, Node root)
    {
        if (document.BrowsingContext?.Top.PointerLockTarget is not { } target || !ReferenceEquals(target.NodeDocument, document))
            return null;

        var candidate = EventDispatcher.Retarget(target, root) as Element;
        return candidate is not null && ReferenceEquals(candidate.GetRoot(), root) ? candidate : null;
    }

    /// <summary>https://w3c.github.io/pointerlock/#dom-element-requestpointerlock</summary>
    public static Task Request(Element element, PointerLockOptions options)
    {
        var document = element.NodeDocument;
        var promise = new TaskCompletionSource(TaskCreationOptions.None);
        if (!document.IsFullyActive || document.RelevantWindow is not { } window || document.BrowsingContext is not { } navigable)
        {
            promise.SetException(new DomException("The document is not fully active", "WrongDocumentError"));
            return promise.Task;
        }

        var top = navigable.Top;
        DomException? error = null;
        if (!element.IsConnected)
        {
            // As in the browsers: the pointer cannot be locked to an element that is not in a document
            error = new DomException("The element is not connected", "WrongDocumentError");
        }
        else if (!top.HasSystemFocus)
        {
            error = new DomException("The document is not focused", "WrongDocumentError");
        }
        else if (!window.HasTransientActivation && !document.HasReleasedPointerLock)
        {
            error = DomException.NotAllowed("requestPointerLock() requires a user activation");
        }
        else if ((document.ActiveSandboxingFlags & SandboxingFlags.PointerLock) != SandboxingFlags.None)
        {
            error = new DomException("The document is sandboxed without the 'allow-pointer-lock' keyword", "SecurityError");
        }

        if (error is not null)
        {
            Fail(document, promise, error);
            return promise.Task;
        }

        // The steps of the lock requests queue
        top.EventLoop.QueueTask(() =>
        {
            if (!document.IsFullyActive || top.IsDestroyed)
                return;

            if (!element.IsConnected || !ReferenceEquals(element.NodeDocument, document) || !top.HasSystemFocus)
            {
                FailNow(document, promise, new DomException("The element was removed from its document, or its document is no longer focused", "WrongDocumentError"));
                return;
            }

            var current = top.PointerLockTarget;
            if (current is not null && !ReferenceEquals(current.NodeDocument, document))
            {
                FailNow(document, promise, DomException.InvalidState("The pointer is locked to an element of another document"));
                return;
            }

            if (current is null || top.PointerLockOptions is not { } currentOptions || currentOptions.UnadjustedMovement != options.UnadjustedMovement)
            {
                // "process the lock request": the host locks its pointer, with or without the adjustments of the platform
                if (top.Options.RequestPointerLock is { } request && !request(top, options))
                {
                    FailNow(document, promise, DomException.NotSupported(options.UnadjustedMovement ? "The pointer cannot be locked without the adjustments of the platform" : "The pointer cannot be locked"));
                    return;
                }

                if (current is null)
                {
                    // The cursor position is the position of the mouse; the pointer lock releases the pointer captures
                    // (https://w3c.github.io/pointerevents/#pointer-lock)
                    top.InputOrNull?.OnPointerLocked();
                }
            }

            top.PointerLockTarget = element;
            top.PointerLockOptions = options;
            document.FireEvent(new Event("pointerlockchange"));
            promise.SetResult();
        });

        return promise.Task;
    }

    private static void Fail(Document document, TaskCompletionSource promise, DomException error)
        => document.QueueElementTask(() => FailNow(document, promise, error));

    private static void FailNow(Document document, TaskCompletionSource promise, DomException error)
    {
        document.FireEvent(new Event("pointerlockerror"));
        promise.SetException(error);
    }

    /// <summary>https://w3c.github.io/pointerlock/#dom-document-exitpointerlock</summary>
    public static void Exit(Document document)
    {
        if (document.BrowsingContext?.Top is not { PointerLockTarget: { } target } top || !ReferenceEquals(target.NodeDocument, document))
            return;

        document.HasReleasedPointerLock = true;
        ExitPointerLock(top);
    }

    /// <summary>
    /// The pointer lock is exited by the user agent: the default unlock gesture (the Escape key), or the loss of the focus
    /// or of the visibility of the window. The document needs a user activation to lock the pointer again.
    /// </summary>
    public static void ExitByUserAgent(BrowsingContext top)
    {
        if (top.PointerLockTarget is not { } target)
            return;

        target.NodeDocument.HasReleasedPointerLock = false;
        ExitPointerLock(top);
    }

    /// <summary>"Pointer lock must be exited if the pointer-lock target becomes disconnected" (https://w3c.github.io/pointerlock/#requirements).</summary>
    public static void OnRemoved(BrowsingContext top, Node removedNode)
    {
        if (top.PointerLockTarget is { } target && removedNode.IsShadowIncludingInclusiveAncestorOf(target))
        {
            ExitPointerLock(top);
        }
    }

    /// <summary>The document is unloaded or destroyed: the pointer lock of one of its elements is exited.</summary>
    public static void OnDocumentUnloaded(Document document)
    {
        if (document.BrowsingContext?.Top is { PointerLockTarget: { } target } top && ReferenceEquals(target.NodeDocument, document))
        {
            ExitPointerLock(top);
        }
    }

    /// <summary>https://w3c.github.io/pointerlock/#dfn-exit-pointer-lock</summary>
    private static void ExitPointerLock(BrowsingContext top)
    {
        if (top.PointerLockTarget is not { } target)
            return;

        // "Exit the pointer lock state by setting pointer-lock target, pointer-lock options, and cursor position to null":
        // the cursor is displayed again at the cursor position
        top.PointerLockTarget = null;
        top.PointerLockOptions = null;
        top.InputOrNull?.OnPointerUnlocked();
        if (!top.IsDestroyed)
        {
            top.Options.ExitPointerLock?.Invoke(top);
        }

        var document = target.NodeDocument;
        document.QueueElementTask(() => document.FireEvent(new Event("pointerlockchange")));
    }
}
