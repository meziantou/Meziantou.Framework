namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The initialization dictionary of a <see cref="CapturedMouseEvent"/> (https://screen-share.github.io/captured-mouse-events/#dom-capturedmouseeventinit).</summary>
/// <seealso href="https://screen-share.github.io/captured-mouse-events/#dom-capturedmouseeventinit"/>
[IdlInterface("CapturedMouseEventInit", "https://screen-share.github.io/captured-mouse-events/#dom-capturedmouseeventinit")]
public sealed class CapturedMouseEventInit : EventInit
{
    /// <summary>Gets the horizontal position of the cursor in the captured surface, or -1 (the default) when the cursor is outside of it.</summary>
    /// <seealso href="https://screen-share.github.io/captured-mouse-events/#dom-capturedmouseeventinit-surfacex"/>
    [IdlMember("surfaceX", "https://screen-share.github.io/captured-mouse-events/#dom-capturedmouseeventinit-surfacex")]
    public int SurfaceX { get; init; } = -1;

    /// <summary>Gets the vertical position of the cursor in the captured surface, or -1 (the default) when the cursor is outside of it.</summary>
    /// <seealso href="https://screen-share.github.io/captured-mouse-events/#dom-capturedmouseeventinit-surfacey"/>
    [IdlMember("surfaceY", "https://screen-share.github.io/captured-mouse-events/#dom-capturedmouseeventinit-surfacey")]
    public int SurfaceY { get; init; } = -1;
}

/// <summary>
/// The event fired at a <see cref="CaptureController"/> when the cursor moves over the captured display surface
/// (<c>capturedmousechange</c>, https://screen-share.github.io/captured-mouse-events/#captured-mouse-change-event). The library
/// does not track the cursor over the captured surfaces: it does not fire the event itself.
/// </summary>
/// <seealso href="https://screen-share.github.io/captured-mouse-events/#dom-capturedmouseevent"/>
[IdlInterface("CapturedMouseEvent", "https://screen-share.github.io/captured-mouse-events/#dom-capturedmouseevent")]
public sealed class CapturedMouseEvent : Event
{
    /// <summary>Creates an event.</summary>
    /// <param name="type">The type of the event.</param>
    /// <param name="eventInitDict">The initialization dictionary.</param>
    /// <exception cref="RangeErrorException">A coordinate is negative, and they are not both -1.</exception>
    /// <seealso href="https://screen-share.github.io/captured-mouse-events/#dom-capturedmouseevent-constructor"/>
    [IdlMember("constructor", "https://screen-share.github.io/captured-mouse-events/#dom-capturedmouseevent-constructor")]
    public CapturedMouseEvent(string type, CapturedMouseEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        var surfaceX = eventInitDict?.SurfaceX ?? -1;
        var surfaceY = eventInitDict?.SurfaceY ?? -1;
        if ((surfaceX < 0 || surfaceY < 0) && (surfaceX, surfaceY) is not (-1, -1))
            throw new RangeErrorException("The coordinates must not be negative, unless they are both -1");

        SurfaceX = surfaceX;
        SurfaceY = surfaceY;
    }

    /// <summary>Gets the horizontal position of the cursor in the captured surface, in pixels, or -1 when the cursor is outside of it.</summary>
    /// <seealso href="https://screen-share.github.io/captured-mouse-events/#dom-capturedmouseevent-surfacex"/>
    [IdlMember("surfaceX", "https://screen-share.github.io/captured-mouse-events/#dom-capturedmouseevent-surfacex")]
    public int SurfaceX { get; }

    /// <summary>Gets the vertical position of the cursor in the captured surface, in pixels, or -1 when the cursor is outside of it.</summary>
    /// <seealso href="https://screen-share.github.io/captured-mouse-events/#dom-capturedmouseevent-surfacey"/>
    [IdlMember("surfaceY", "https://screen-share.github.io/captured-mouse-events/#dom-capturedmouseevent-surfacey")]
    public int SurfaceY { get; }
}

// Captured Mouse Events (https://screen-share.github.io/captured-mouse-events/#capture-controller-extensions)
public sealed partial class CaptureController
{
    /// <summary>Gets or sets the event handler of the <c>capturedmousechange</c> event (<see cref="CapturedMouseEvent"/>).</summary>
    /// <seealso href="https://screen-share.github.io/captured-mouse-events/#dom-capturecontroller-oncapturedmousechange"/>
    [IdlMember("oncapturedmousechange", "https://screen-share.github.io/captured-mouse-events/#dom-capturecontroller-oncapturedmousechange")]
    public Action<Event>? OnCapturedMouseChange
    {
        get => GetEventHandler("capturedmousechange");
        set => SetEventHandler("capturedmousechange", value);
    }
}
