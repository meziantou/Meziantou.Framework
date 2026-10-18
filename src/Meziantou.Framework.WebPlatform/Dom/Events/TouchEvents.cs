using System.Collections;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://w3c.github.io/touch-events/#dom-touchtype</summary>
[IdlInterface("TouchType", "https://w3c.github.io/touch-events/#dom-touchtype")]
public enum TouchType
{
    /// <summary>A direct touch from a finger on the screen.</summary>
    [IdlMember("direct", "https://w3c.github.io/touch-events/#dom-touchtype-direct")]
    Direct,

    /// <summary>A touch from a stylus or pen device.</summary>
    [IdlMember("stylus", "https://w3c.github.io/touch-events/#dom-touchtype-stylus")]
    Stylus,
}

/// <summary>https://w3c.github.io/touch-events/#dom-touchinit</summary>
[IdlInterface("TouchInit", "https://w3c.github.io/touch-events/#dom-touchinit")]
public sealed class TouchInit
{
    /// <summary>Gets the identification number of the touch point.</summary>
    [IdlMember("identifier", "https://w3c.github.io/touch-events/#dom-touchinit-identifier")]
    public required int Identifier { get; init; }

    /// <summary>Gets the target of the touch point.</summary>
    [IdlMember("target", "https://w3c.github.io/touch-events/#dom-touchinit-target")]
    public required EventTarget Target { get; init; }

    /// <summary>Gets the horizontal coordinate of the point relative to the viewport.</summary>
    [IdlMember("clientX", "https://w3c.github.io/touch-events/#dom-touchinit-clientx")]
    public double ClientX { get; init; }

    /// <summary>Gets the vertical coordinate of the point relative to the viewport.</summary>
    [IdlMember("clientY", "https://w3c.github.io/touch-events/#dom-touchinit-clienty")]
    public double ClientY { get; init; }

    /// <summary>Gets the horizontal coordinate of the point relative to the screen.</summary>
    [IdlMember("screenX", "https://w3c.github.io/touch-events/#dom-touchinit-screenx")]
    public double ScreenX { get; init; }

    /// <summary>Gets the vertical coordinate of the point relative to the screen.</summary>
    [IdlMember("screenY", "https://w3c.github.io/touch-events/#dom-touchinit-screeny")]
    public double ScreenY { get; init; }

    /// <summary>Gets the horizontal coordinate of the point relative to the page.</summary>
    [IdlMember("pageX", "https://w3c.github.io/touch-events/#dom-touchinit-pagex")]
    public double PageX { get; init; }

    /// <summary>Gets the vertical coordinate of the point relative to the page.</summary>
    [IdlMember("pageY", "https://w3c.github.io/touch-events/#dom-touchinit-pagey")]
    public double PageY { get; init; }

    /// <summary>Gets the radius of the ellipse of the contact along its major axis.</summary>
    [IdlMember("radiusX", "https://w3c.github.io/touch-events/#dom-touchinit-radiusx")]
    public float RadiusX { get; init; }

    /// <summary>Gets the radius of the ellipse of the contact along its minor axis.</summary>
    [IdlMember("radiusY", "https://w3c.github.io/touch-events/#dom-touchinit-radiusy")]
    public float RadiusY { get; init; }

    /// <summary>Gets the angle, in degrees, of the ellipse of the contact.</summary>
    [IdlMember("rotationAngle", "https://w3c.github.io/touch-events/#dom-touchinit-rotationangle")]
    public float RotationAngle { get; init; }

    /// <summary>Gets the relative force of the contact, between 0 and 1.</summary>
    [IdlMember("force", "https://w3c.github.io/touch-events/#dom-touchinit-force")]
    public float Force { get; init; }

    /// <summary>Gets the altitude, in radians, of the transducer.</summary>
    [IdlMember("altitudeAngle", "https://w3c.github.io/touch-events/#dom-touchinit-altitudeangle")]
    public double AltitudeAngle { get; init; }

    /// <summary>Gets the azimuth angle, in radians, of the transducer.</summary>
    [IdlMember("azimuthAngle", "https://w3c.github.io/touch-events/#dom-touchinit-azimuthangle")]
    public double AzimuthAngle { get; init; }

    /// <summary>Gets the type of the device that produced the touch.</summary>
    [IdlMember("touchType", "https://w3c.github.io/touch-events/#dom-touchinit-touchtype")]
    public TouchType TouchType { get; init; }
}

/// <summary>https://w3c.github.io/touch-events/#touch-interface</summary>
[IdlInterface("Touch", "https://w3c.github.io/touch-events/#touch-interface")]
public sealed class Touch
{
    /// <summary>Creates a touch point from the given initialization dictionary.</summary>
    [IdlMember("constructor", "https://w3c.github.io/touch-events/#dom-touch-constructor")]
    public Touch(TouchInit touchInitDict)
    {
        ArgumentNullException.ThrowIfNull(touchInitDict);
        Identifier = touchInitDict.Identifier;
        Target = touchInitDict.Target;
        ClientX = touchInitDict.ClientX;
        ClientY = touchInitDict.ClientY;
        ScreenX = touchInitDict.ScreenX;
        ScreenY = touchInitDict.ScreenY;
        PageX = touchInitDict.PageX;
        PageY = touchInitDict.PageY;
        RadiusX = touchInitDict.RadiusX;
        RadiusY = touchInitDict.RadiusY;
        RotationAngle = touchInitDict.RotationAngle;
        Force = touchInitDict.Force;
        AltitudeAngle = (float)touchInitDict.AltitudeAngle;
        AzimuthAngle = (float)touchInitDict.AzimuthAngle;
        TouchType = touchInitDict.TouchType;
    }

    /// <summary>Creates a copy of a touch point with another target (see <see cref="TouchEvent.Touches"/>).</summary>
    private Touch(Touch touch, EventTarget target)
    {
        Identifier = touch.Identifier;
        Target = target;
        ClientX = touch.ClientX;
        ClientY = touch.ClientY;
        ScreenX = touch.ScreenX;
        ScreenY = touch.ScreenY;
        PageX = touch.PageX;
        PageY = touch.PageY;
        RadiusX = touch.RadiusX;
        RadiusY = touch.RadiusY;
        RotationAngle = touch.RotationAngle;
        Force = touch.Force;
        AltitudeAngle = touch.AltitudeAngle;
        AzimuthAngle = touch.AzimuthAngle;
        TouchType = touch.TouchType;
    }

    internal Touch WithTarget(EventTarget target) => new(this, target);

    /// <summary>Gets the identification number of the touch point, the same for all the events of the contact.</summary>
    [IdlMember("identifier", "https://w3c.github.io/touch-events/#dom-touch-identifier")]
    public int Identifier { get; }

    /// <summary>Gets the target the touch point started on (even when it moved outside of it).</summary>
    [IdlMember("target", "https://w3c.github.io/touch-events/#dom-touch-target")]
    public EventTarget Target { get; }

    /// <summary>Gets the horizontal coordinate of the point relative to the screen.</summary>
    [IdlMember("screenX", "https://w3c.github.io/touch-events/#dom-touch-screenx")]
    public double ScreenX { get; }

    /// <summary>Gets the vertical coordinate of the point relative to the screen.</summary>
    [IdlMember("screenY", "https://w3c.github.io/touch-events/#dom-touch-screeny")]
    public double ScreenY { get; }

    /// <summary>Gets the horizontal coordinate of the point relative to the viewport.</summary>
    [IdlMember("clientX", "https://w3c.github.io/touch-events/#dom-touch-clientx")]
    public double ClientX { get; }

    /// <summary>Gets the vertical coordinate of the point relative to the viewport.</summary>
    [IdlMember("clientY", "https://w3c.github.io/touch-events/#dom-touch-clienty")]
    public double ClientY { get; }

    /// <summary>Gets the horizontal coordinate of the point relative to the page.</summary>
    [IdlMember("pageX", "https://w3c.github.io/touch-events/#dom-touch-pagex")]
    public double PageX { get; }

    /// <summary>Gets the vertical coordinate of the point relative to the page.</summary>
    [IdlMember("pageY", "https://w3c.github.io/touch-events/#dom-touch-pagey")]
    public double PageY { get; }

    /// <summary>Gets the radius of the ellipse of the contact along its major axis.</summary>
    [IdlMember("radiusX", "https://w3c.github.io/touch-events/#dom-touch-radiusx")]
    public float RadiusX { get; }

    /// <summary>Gets the radius of the ellipse of the contact along its minor axis.</summary>
    [IdlMember("radiusY", "https://w3c.github.io/touch-events/#dom-touch-radiusy")]
    public float RadiusY { get; }

    /// <summary>Gets the angle, in degrees, of the ellipse of the contact.</summary>
    [IdlMember("rotationAngle", "https://w3c.github.io/touch-events/#dom-touch-rotationangle")]
    public float RotationAngle { get; }

    /// <summary>Gets the relative force of the contact, between 0 and 1.</summary>
    [IdlMember("force", "https://w3c.github.io/touch-events/#dom-touch-force")]
    public float Force { get; }

    /// <summary>Gets the altitude, in radians, of the transducer.</summary>
    [IdlMember("altitudeAngle", "https://w3c.github.io/touch-events/#dom-touch-altitudeangle")]
    public float AltitudeAngle { get; }

    /// <summary>Gets the azimuth angle, in radians, of the transducer.</summary>
    [IdlMember("azimuthAngle", "https://w3c.github.io/touch-events/#dom-touch-azimuthangle")]
    public float AzimuthAngle { get; }

    /// <summary>Gets the type of the device that produced the touch.</summary>
    [IdlMember("touchType", "https://w3c.github.io/touch-events/#dom-touch-touchtype")]
    public TouchType TouchType { get; }
}

/// <summary>https://w3c.github.io/touch-events/#touchlist-interface</summary>
[IdlInterface("TouchList", "https://w3c.github.io/touch-events/#touchlist-interface")]
public sealed class TouchList : IReadOnlyList<Touch>
{
    private readonly Touch[] _items;

    /// <summary>Creates a list that contains the given touch points.</summary>
    [NotInIdl]
    public TouchList(IEnumerable<Touch> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = [.. items];
    }

    /// <summary>Gets the number of touch points in the list.</summary>
    [IdlMember("length", "https://w3c.github.io/touch-events/#dom-touchlist-length")]
    public uint Length => (uint)_items.Length;

    /// <summary>Gets the number of touch points in the list (the .NET counterpart of <c>length</c>).</summary>
    [NotInIdl]
    public int Count => _items.Length;

    /// <summary>Returns the touch point at the given index, or null when the index is out of range.</summary>
    [IdlMember("item", "https://w3c.github.io/touch-events/#dom-touchlist-item")]
    public Touch? Item(uint index) => index < (uint)_items.Length ? _items[index] : null;

    /// <summary>Gets the touch point at the given index.</summary>
    [NotInIdl]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public Touch this[int index] => _items[index];

    /// <inheritdoc/>
    [NotInIdl]
    public IEnumerator<Touch> GetEnumerator() => ((IEnumerable<Touch>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
}

/// <summary>https://w3c.github.io/touch-events/#dom-toucheventinit</summary>
[IdlInterface("TouchEventInit", "https://w3c.github.io/touch-events/#dom-toucheventinit")]
public sealed class TouchEventInit : EventModifierInit
{
    /// <summary>Gets the touch points that are in contact with the surface.</summary>
    [IdlMember("touches", "https://w3c.github.io/touch-events/#dom-toucheventinit-touches")]
    public IReadOnlyList<Touch> Touches { get; init; } = [];

    /// <summary>Gets the touch points that are in contact with the surface and started on the target of the event.</summary>
    [IdlMember("targetTouches", "https://w3c.github.io/touch-events/#dom-toucheventinit-targettouches")]
    public IReadOnlyList<Touch> TargetTouches { get; init; } = [];

    /// <summary>Gets the touch points that changed.</summary>
    [IdlMember("changedTouches", "https://w3c.github.io/touch-events/#dom-toucheventinit-changedtouches")]
    public IReadOnlyList<Touch> ChangedTouches { get; init; } = [];
}

/// <summary>https://w3c.github.io/touch-events/#touchevent-interface</summary>
[IdlInterface("TouchEvent", "https://w3c.github.io/touch-events/#touchevent-interface")]
public sealed class TouchEvent : UIEvent
{
    private readonly ModifierState _modifiers;

    /// <summary>Creates a touch event of the given type with the given initialization dictionary.</summary>
    [IdlMember("constructor", "https://w3c.github.io/touch-events/#dom-touchevent-constructor")]
    public TouchEvent(string type, TouchEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        _modifiers = ModifierState.From(eventInitDict);
        _touches = new TouchList(eventInitDict?.Touches ?? []);
        _targetTouches = new TouchList(eventInitDict?.TargetTouches ?? []);
        _changedTouches = new TouchList(eventInitDict?.ChangedTouches ?? []);
    }

    private readonly TouchList _touches;
    private readonly TouchList _targetTouches;
    private readonly TouchList _changedTouches;

    // The lists and the touch points retargeted against the trees of the current targets of the dispatch
    private Dictionary<(TouchList List, EventTarget Root), TouchList>? _retargetedLists;
    private Dictionary<(Touch Touch, EventTarget Target), Touch>? _retargetedTouches;

    /// <summary>
    /// Gets the touch points that are in contact with the surface. As in Chromium and Gecko, while the event is dispatched, the
    /// targets of the touch points are retargeted against the current target of the event (https://dom.spec.whatwg.org/#retarget),
    /// as the target of the event is: a listener outside of a shadow tree does not see the nodes of the shadow tree (the
    /// touch points whose target changes are copies, the same ones for the listeners of a tree).
    /// </summary>
    [IdlMember("touches", "https://w3c.github.io/touch-events/#dom-touchevent-touches")]
    public TouchList Touches => GetRetargetedList(_touches);

    /// <summary>Gets the touch points that are in contact with the surface and started on the target of the event (retargeted, see <see cref="Touches"/>).</summary>
    [IdlMember("targetTouches", "https://w3c.github.io/touch-events/#dom-touchevent-targettouches")]
    public TouchList TargetTouches => GetRetargetedList(_targetTouches);

    /// <summary>Gets the touch points that changed: the ones that started, moved or ended (retargeted, see <see cref="Touches"/>).</summary>
    [IdlMember("changedTouches", "https://w3c.github.io/touch-events/#dom-touchevent-changedtouches")]
    public TouchList ChangedTouches => GetRetargetedList(_changedTouches);

    private TouchList GetRetargetedList(TouchList list)
    {
        if (!DispatchFlag || CurrentTarget is not { } currentTarget)
            return list;

        var retarget = false;
        foreach (var touch in list)
        {
            if (!ReferenceEquals(EventDispatcher.Retarget(touch.Target, currentTarget), touch.Target))
            {
                retarget = true;
                break;
            }
        }

        if (!retarget)
            return list;

        // The targets only depend on the tree of the current target (a window is outside of every tree)
        var root = currentTarget is Node node ? node.GetRoot() : currentTarget;
        _retargetedLists ??= [];
        if (!_retargetedLists.TryGetValue((list, root), out var result))
        {
            _retargetedTouches ??= [];
            var touches = new Touch[list.Count];
            for (var i = 0; i < touches.Length; i++)
            {
                var touch = list[i];
                var target = EventDispatcher.Retarget(touch.Target, currentTarget)!;
                if (!ReferenceEquals(target, touch.Target) && !_retargetedTouches.TryGetValue((touch, target), out touch!))
                {
                    touch = list[i].WithTarget(target);
                    _retargetedTouches.Add((list[i], target), touch);
                }

                touches[i] = touch;
            }

            result = new TouchList(touches);
            _retargetedLists.Add((list, root), result);
        }

        return result;
    }

    /// <summary>Gets a value indicating whether the Alt key was active.</summary>
    [IdlMember("altKey", "https://w3c.github.io/touch-events/#dom-touchevent-altkey")]
    public bool AltKey => _modifiers.Alt;

    /// <summary>Gets a value indicating whether the Meta key was active.</summary>
    [IdlMember("metaKey", "https://w3c.github.io/touch-events/#dom-touchevent-metakey")]
    public bool MetaKey => _modifiers.Meta;

    /// <summary>Gets a value indicating whether the Control key was active.</summary>
    [IdlMember("ctrlKey", "https://w3c.github.io/touch-events/#dom-touchevent-ctrlkey")]
    public bool CtrlKey => _modifiers.Ctrl;

    /// <summary>Gets a value indicating whether the Shift key was active.</summary>
    [IdlMember("shiftKey", "https://w3c.github.io/touch-events/#dom-touchevent-shiftkey")]
    public bool ShiftKey => _modifiers.Shift;

    /// <summary>Returns whether the given modifier key was active when the event occurred.</summary>
    [IdlMember("getModifierState", "https://w3c.github.io/touch-events/#dom-touchevent-getmodifierstate")]
    public bool GetModifierState(string keyArg)
    {
        ArgumentNullException.ThrowIfNull(keyArg);
        return _modifiers.Get(keyArg);
    }
}
