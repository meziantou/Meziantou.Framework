using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

// UI Events (https://w3c.github.io/uievents/). There is no user interaction, so these events are only created by
// constructors and document.createEvent().

/// <summary>https://w3c.github.io/uievents/#dictdef-uieventinit</summary>
/// <seealso href="https://w3c.github.io/uievents/#dictdef-uieventinit"/>
[IdlInterface("UIEventInit", "https://w3c.github.io/uievents/#dictdef-uieventinit")]
public class UIEventInit : EventInit
{
    /// <summary>The Window of the event; documents have no browsing context, so it is an arbitrary event target provided by the host.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-uieventinit-view"/>
    [IdlMember("view", "https://w3c.github.io/uievents/#dom-uieventinit-view")]
    public EventTarget? View { get; init; }

    /// <summary>
    /// Gets the detail of the event, whose meaning depends on the event type (for example the click count).
    /// </summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-uieventinit-detail"/>
    [IdlMember("detail", "https://w3c.github.io/uievents/#dom-uieventinit-detail")]
    public int Detail { get; init; }

    /// <summary>Gets the legacy numeric code of the key or button of the event.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-uieventinit-which"/>
    [IdlMember("which", "https://w3c.github.io/uievents/#dom-uieventinit-which")]
    public uint Which { get; init; }

    /// <summary>Gets the pseudo-element that is the target of the interaction, or null (https://github.com/w3c/uievents/pull/413).</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-uieventinit-pseudotarget"/>
    [IdlMember("pseudoTarget", "https://w3c.github.io/uievents/#dom-uieventinit-pseudotarget")]
    [Experimental(ExperimentalApis.UIEventPseudoTarget, UrlFormat = ExperimentalApis.UrlFormat)]
    public Css.Om.CssPseudoElement? PseudoTarget { get; init; }
}

/// <summary>https://w3c.github.io/uievents/#uievent</summary>
/// <seealso href="https://w3c.github.io/uievents/#uievent"/>
[IdlInterface("UIEvent", "https://w3c.github.io/uievents/#uievent")]
public class UIEvent : Event
{
    /// <summary>Creates a UI event of the given type with the given initialization dictionary.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-uievent-uievent"/>
    [IdlMember("constructor", "https://w3c.github.io/uievents/#dom-uievent-uievent")]
    public UIEvent(string type, UIEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        View = eventInitDict?.View;
        Detail = eventInitDict?.Detail ?? 0;
        Which = eventInitDict?.Which ?? 0;
        PseudoTargetValue = eventInitDict?.PseudoTarget;
    }

    private protected UIEvent()
    {
    }

    internal static UIEvent CreateUninitializedUIEvent() => new();

    /// <summary>Gets the window in which the event occurred, or null.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-uievent-view"/>
    [IdlMember("view", "https://w3c.github.io/uievents/#dom-uievent-view")]
    public EventTarget? View { get; private protected set; }

    /// <summary>
    /// Gets the detail of the event, whose meaning depends on the event type (for example the click count).
    /// </summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-uievent-detail"/>
    [IdlMember("detail", "https://w3c.github.io/uievents/#dom-uievent-detail")]
    public int Detail { get; private protected set; }

    /// <summary>Gets the legacy numeric code of the key or button of the event.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-uievent-which"/>
    [IdlMember("which", "https://w3c.github.io/uievents/#dom-uievent-which")]
    public uint Which { get; private protected set; }

    /// <summary>
    /// The pseudo-element that is the target of the interaction (the pseudoTarget internal slot of
    /// https://github.com/w3c/uievents/pull/413), or null; its ultimate originating element is the pseudoTargetOrigin slot.
    /// </summary>
    internal Css.Om.CssPseudoElement? PseudoTargetValue { get; set; }

    /// <summary>
    /// Gets the pseudo-element that is the target of the interaction (https://github.com/w3c/uievents/pull/413): null when the
    /// target is not a pseudo-element, outside of the dispatch, and for the listeners of a shadow tree the originating element is
    /// retargeted out of (the pseudo-element is in the tree of its originating element).
    /// </summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-uievent-pseudotarget"/>
    [IdlMember("pseudoTarget", "https://w3c.github.io/uievents/#dom-uievent-pseudotarget")]
    [Experimental(ExperimentalApis.UIEventPseudoTarget, UrlFormat = ExperimentalApis.UrlFormat)]
    public Css.Om.CssPseudoElement? PseudoTarget
    {
        get
        {
            // "If currentTarget is null, then return null. [...] Let retargeted be the result of retargeting origin against
            // currentTarget. If retargeted is not origin, then return null."
            if (PseudoTargetValue is not { } pseudoTarget || CurrentTarget is not { } currentTarget)
                return null;

            var origin = pseudoTarget.Element;
            return ReferenceEquals(EventDispatcher.Retarget(origin, currentTarget), origin) ? pseudoTarget : null;
        }
    }

    /// <summary>https://w3c.github.io/uievents/#dom-uievent-inituievent</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-uievent-inituievent"/>
    [IdlMember("initUIEvent", "https://w3c.github.io/uievents/#dom-uievent-inituievent")]
    public void InitUIEvent(string typeArg, bool bubblesArg = false, bool cancelableArg = false, EventTarget? viewArg = null, int detailArg = 0)
    {
        ArgumentNullException.ThrowIfNull(typeArg);
        if (DispatchFlag)
            return;

        Initialize(typeArg, bubblesArg, cancelableArg);
        View = viewArg;
        Detail = detailArg;
    }
}

/// <summary>https://w3c.github.io/uievents/#dictdef-focuseventinit</summary>
/// <seealso href="https://w3c.github.io/uievents/#dictdef-focuseventinit"/>
[IdlInterface("FocusEventInit", "https://w3c.github.io/uievents/#dictdef-focuseventinit")]
public sealed class FocusEventInit : UIEventInit
{
    /// <summary>
    /// Gets the secondary target of the focus change (the element losing or receiving the focus), or null.
    /// </summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-focuseventinit-relatedtarget"/>
    [IdlMember("relatedTarget", "https://w3c.github.io/uievents/#dom-focuseventinit-relatedtarget")]
    public EventTarget? RelatedTarget { get; init; }
}

/// <summary>https://w3c.github.io/uievents/#focusevent</summary>
/// <seealso href="https://w3c.github.io/uievents/#focusevent"/>
[IdlInterface("FocusEvent", "https://w3c.github.io/uievents/#focusevent")]
public sealed class FocusEvent : UIEvent
{
    /// <summary>Creates a focus event of the given type with the given initialization dictionary.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-focusevent-focusevent"/>
    [IdlMember("constructor", "https://w3c.github.io/uievents/#dom-focusevent-focusevent")]
    public FocusEvent(string type, FocusEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        base.RelatedTarget = eventInitDict?.RelatedTarget;
    }

    private FocusEvent()
    {
    }

    internal static FocusEvent CreateUninitializedFocusEvent() => new();

    /// <summary>
    /// Gets the secondary target of the focus change (the element losing or receiving the focus), or null.
    /// </summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-focusevent-relatedtarget"/>
    [IdlMember("relatedTarget", "https://w3c.github.io/uievents/#dom-focusevent-relatedtarget")]
    public new EventTarget? RelatedTarget => base.RelatedTarget;
}

/// <summary>https://w3c.github.io/uievents/#dictdef-eventmodifierinit</summary>
/// <seealso href="https://w3c.github.io/uievents/#dictdef-eventmodifierinit"/>
[IdlInterface("EventModifierInit", "https://w3c.github.io/uievents/#dictdef-eventmodifierinit")]
public class EventModifierInit : UIEventInit
{
    /// <summary>Gets a value indicating whether the Control key was active.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-eventmodifierinit-ctrlkey"/>
    [IdlMember("ctrlKey", "https://w3c.github.io/uievents/#dom-eventmodifierinit-ctrlkey")]
    public bool CtrlKey { get; init; }

    /// <summary>Gets a value indicating whether the Shift key was active.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-eventmodifierinit-shiftkey"/>
    [IdlMember("shiftKey", "https://w3c.github.io/uievents/#dom-eventmodifierinit-shiftkey")]
    public bool ShiftKey { get; init; }

    /// <summary>Gets a value indicating whether the Alt key was active.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-eventmodifierinit-altkey"/>
    [IdlMember("altKey", "https://w3c.github.io/uievents/#dom-eventmodifierinit-altkey")]
    public bool AltKey { get; init; }

    /// <summary>Gets a value indicating whether the Meta key was active.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-eventmodifierinit-metakey"/>
    [IdlMember("metaKey", "https://w3c.github.io/uievents/#dom-eventmodifierinit-metakey")]
    public bool MetaKey { get; init; }

    /// <summary>Gets a value indicating whether the AltGraph modifier was active.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-eventmodifierinit-modifieraltgraph"/>
    [IdlMember("modifierAltGraph", "https://w3c.github.io/uievents/#dom-eventmodifierinit-modifieraltgraph")]
    public bool ModifierAltGraph { get; init; }

    /// <summary>Gets a value indicating whether the CapsLock modifier was active.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-eventmodifierinit-modifiercapslock"/>
    [IdlMember("modifierCapsLock", "https://w3c.github.io/uievents/#dom-eventmodifierinit-modifiercapslock")]
    public bool ModifierCapsLock { get; init; }

    /// <summary>Gets a value indicating whether the Fn modifier was active.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-eventmodifierinit-modifierfn"/>
    [IdlMember("modifierFn", "https://w3c.github.io/uievents/#dom-eventmodifierinit-modifierfn")]
    public bool ModifierFn { get; init; }

    /// <summary>Gets a value indicating whether the FnLock modifier was active.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-eventmodifierinit-modifierfnlock"/>
    [IdlMember("modifierFnLock", "https://w3c.github.io/uievents/#dom-eventmodifierinit-modifierfnlock")]
    public bool ModifierFnLock { get; init; }

    /// <summary>Gets a value indicating whether the Hyper modifier was active.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-eventmodifierinit-modifierhyper"/>
    [IdlMember("modifierHyper", "https://w3c.github.io/uievents/#dom-eventmodifierinit-modifierhyper")]
    public bool ModifierHyper { get; init; }

    /// <summary>Gets a value indicating whether the NumLock modifier was active.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-eventmodifierinit-modifiernumlock"/>
    [IdlMember("modifierNumLock", "https://w3c.github.io/uievents/#dom-eventmodifierinit-modifiernumlock")]
    public bool ModifierNumLock { get; init; }

    /// <summary>Gets a value indicating whether the ScrollLock modifier was active.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-eventmodifierinit-modifierscrolllock"/>
    [IdlMember("modifierScrollLock", "https://w3c.github.io/uievents/#dom-eventmodifierinit-modifierscrolllock")]
    public bool ModifierScrollLock { get; init; }

    /// <summary>Gets a value indicating whether the Super modifier was active.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-eventmodifierinit-modifiersuper"/>
    [IdlMember("modifierSuper", "https://w3c.github.io/uievents/#dom-eventmodifierinit-modifiersuper")]
    public bool ModifierSuper { get; init; }

    /// <summary>Gets a value indicating whether the Symbol modifier was active.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-eventmodifierinit-modifiersymbol"/>
    [IdlMember("modifierSymbol", "https://w3c.github.io/uievents/#dom-eventmodifierinit-modifiersymbol")]
    public bool ModifierSymbol { get; init; }

    /// <summary>Gets a value indicating whether the SymbolLock modifier was active.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-eventmodifierinit-modifiersymbollock"/>
    [IdlMember("modifierSymbolLock", "https://w3c.github.io/uievents/#dom-eventmodifierinit-modifiersymbollock")]
    public bool ModifierSymbolLock { get; init; }
}

/// <summary>The modifier keys state shared by MouseEvent and KeyboardEvent (https://w3c.github.io/uievents/#dom-mouseevent-getmodifierstate).</summary>
internal struct ModifierState
{
    public bool Ctrl;
    public bool Shift;
    public bool Alt;
    public bool Meta;
    public bool AltGraph;
    public bool CapsLock;
    public bool Fn;
    public bool FnLock;
    public bool Hyper;
    public bool NumLock;
    public bool ScrollLock;
    public bool Super;
    public bool Symbol;
    public bool SymbolLock;

    public static ModifierState From(EventModifierInit? init) => init is null ? default : new ModifierState
    {
        Ctrl = init.CtrlKey,
        Shift = init.ShiftKey,
        Alt = init.AltKey,
        Meta = init.MetaKey,
        AltGraph = init.ModifierAltGraph,
        CapsLock = init.ModifierCapsLock,
        Fn = init.ModifierFn,
        FnLock = init.ModifierFnLock,
        Hyper = init.ModifierHyper,
        NumLock = init.ModifierNumLock,
        ScrollLock = init.ModifierScrollLock,
        Super = init.ModifierSuper,
        Symbol = init.ModifierSymbol,
        SymbolLock = init.ModifierSymbolLock,
    };

    public readonly bool Get(string key) => key switch
    {
        "Control" => Ctrl,
        "Shift" => Shift,
        "Alt" => Alt,
        "Meta" => Meta,
        "AltGraph" => AltGraph,
        "CapsLock" => CapsLock,
        "Fn" => Fn,
        "FnLock" => FnLock,
        "Hyper" => Hyper,
        "NumLock" => NumLock,
        "ScrollLock" => ScrollLock,
        "Super" => Super,
        "Symbol" => Symbol,
        "SymbolLock" => SymbolLock,
        _ => false,
    };
}

/// <summary>https://w3c.github.io/uievents/#dictdef-mouseeventinit</summary>
/// <seealso href="https://w3c.github.io/uievents/#dictdef-mouseeventinit"/>
[IdlInterface("MouseEventInit", "https://w3c.github.io/uievents/#dictdef-mouseeventinit")]
public class MouseEventInit : EventModifierInit
{
    /// <summary>Gets the horizontal coordinate of the pointer relative to the screen.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-mouseeventinit-screenx"/>
    [IdlMember("screenX", "https://w3c.github.io/uievents/#dom-mouseeventinit-screenx")]
    public double ScreenX { get; init; }

    /// <summary>Gets the vertical coordinate of the pointer relative to the screen.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-mouseeventinit-screeny"/>
    [IdlMember("screenY", "https://w3c.github.io/uievents/#dom-mouseeventinit-screeny")]
    public double ScreenY { get; init; }

    /// <summary>Gets the horizontal coordinate of the pointer relative to the viewport.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-mouseeventinit-clientx"/>
    [IdlMember("clientX", "https://w3c.github.io/uievents/#dom-mouseeventinit-clientx")]
    public double ClientX { get; init; }

    /// <summary>Gets the vertical coordinate of the pointer relative to the viewport.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-mouseeventinit-clienty"/>
    [IdlMember("clientY", "https://w3c.github.io/uievents/#dom-mouseeventinit-clienty")]
    public double ClientY { get; init; }

    /// <summary>
    /// Gets the button that changed state: 0 for the main button, 1 for the auxiliary button, 2 for the secondary
    /// button.
    /// </summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-mouseeventinit-button"/>
    [IdlMember("button", "https://w3c.github.io/uievents/#dom-mouseeventinit-button")]
    public short Button { get; init; }

    /// <summary>Gets the bit mask of the buttons that are pressed.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-mouseeventinit-buttons"/>
    [IdlMember("buttons", "https://w3c.github.io/uievents/#dom-mouseeventinit-buttons")]
    public ushort Buttons { get; init; }

    /// <summary>Gets the secondary target of the event (for example the element the pointer left), or null.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-mouseeventinit-relatedtarget"/>
    [IdlMember("relatedTarget", "https://w3c.github.io/uievents/#dom-mouseeventinit-relatedtarget")]
    public EventTarget? RelatedTarget { get; init; }

    /// <summary>Gets the horizontal distance of the pointer movement since the previous event.</summary>
    /// <seealso href="https://w3c.github.io/pointerlock/#dom-mouseeventinit-movementx"/>
    [IdlMember("movementX", "https://w3c.github.io/pointerlock/#dom-mouseeventinit-movementx")]
    public double MovementX { get; init; }

    /// <summary>Gets the vertical distance of the pointer movement since the previous event.</summary>
    /// <seealso href="https://w3c.github.io/pointerlock/#dom-mouseeventinit-movementy"/>
    [IdlMember("movementY", "https://w3c.github.io/pointerlock/#dom-mouseeventinit-movementy")]
    public double MovementY { get; init; }
}

/// <summary>https://w3c.github.io/uievents/#mouseevent</summary>
/// <seealso href="https://w3c.github.io/uievents/#mouseevent"/>
[IdlInterface("MouseEvent", "https://w3c.github.io/uievents/#mouseevent")]
public class MouseEvent : UIEvent
{
    private ModifierState _modifiers;

    // The window of the realm the event was created in: "the event's associated Window object" of pageX and pageY
    private readonly Window? _realmWindow = Document.CurrentGlobalWindow;

    // The coordinates relative to the target computed while the event was dispatched (offsetX/offsetY, layerX/layerY)
    private (double X, double Y)? _offset;
    private (double X, double Y)? _layer;

    /// <summary>Creates a mouse event of the given type with the given initialization dictionary.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-mouseevent-mouseevent"/>
    [IdlMember("constructor", "https://w3c.github.io/uievents/#dom-mouseevent-mouseevent")]
    public MouseEvent(string type, MouseEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        _modifiers = ModifierState.From(eventInitDict);
        if (eventInitDict is not null)
        {
            // https://w3c.github.io/pointerevents/#event-coordinates: the coordinates are fractional for the pointer events
            // other than click, auxclick and contextmenu, and integers (as in the browsers) for the other mouse events
            var fractional = HasFractionalCoordinates(type);
            ScreenX = fractional ? eventInitDict.ScreenX : Math.Floor(eventInitDict.ScreenX);
            ScreenY = fractional ? eventInitDict.ScreenY : Math.Floor(eventInitDict.ScreenY);
            ClientX = fractional ? eventInitDict.ClientX : Math.Floor(eventInitDict.ClientX);
            ClientY = fractional ? eventInitDict.ClientY : Math.Floor(eventInitDict.ClientY);
            Button = eventInitDict.Button;
            Buttons = eventInitDict.Buttons;
            MovementX = eventInitDict.MovementX;
            MovementY = eventInitDict.MovementY;
            base.RelatedTarget = eventInitDict.RelatedTarget;
        }
    }

    private protected MouseEvent()
    {
    }

    internal static MouseEvent CreateUninitializedMouseEvent() => new();

    /// <summary>Whether the coordinates of the event are not rounded to integers: a pointer event that is not click, auxclick or contextmenu.</summary>
    private bool HasFractionalCoordinates(string type) => this is PointerEvent && type is not ("click" or "auxclick" or "contextmenu");

    /// <summary>Gets the horizontal coordinate of the pointer relative to the screen.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-mouseevent-screenx"/>
    [IdlMember("screenX", "https://w3c.github.io/uievents/#dom-mouseevent-screenx")]
    public double ScreenX { get; private set; }

    /// <summary>Gets the vertical coordinate of the pointer relative to the screen.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-mouseevent-screeny"/>
    [IdlMember("screenY", "https://w3c.github.io/uievents/#dom-mouseevent-screeny")]
    public double ScreenY { get; private set; }

    /// <summary>Gets the horizontal coordinate of the pointer relative to the viewport.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-mouseevent-clientx"/>
    [IdlMember("clientX", "https://w3c.github.io/uievents/#dom-mouseevent-clientx")]
    public double ClientX { get; private set; }

    /// <summary>Gets the vertical coordinate of the pointer relative to the viewport.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-mouseevent-clienty"/>
    [IdlMember("clientY", "https://w3c.github.io/uievents/#dom-mouseevent-clienty")]
    public double ClientY { get; private set; }

    /// <summary>
    /// Gets the horizontal coordinate of the pointer relative to the border box of the nearest positioned inclusive ancestor of
    /// the target while the event is dispatched (https://w3c.github.io/uievents/#dom-mouseevent-layerx, a legacy attribute), or
    /// <see cref="PageX"/>.
    /// </summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-mouseevent-layerx"/>
    [IdlMember("layerX", "https://w3c.github.io/uievents/#dom-mouseevent-layerx")]
    public int LayerX => (int)Math.Floor(GetLayerPosition()?.X ?? PageX);

    /// <summary>Gets the vertical coordinate of the pointer relative to the nearest positioned ancestor (https://w3c.github.io/uievents/#dom-mouseevent-layery): see <see cref="LayerX"/>.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-mouseevent-layery"/>
    [IdlMember("layerY", "https://w3c.github.io/uievents/#dom-mouseevent-layery")]
    public int LayerY => (int)Math.Floor(GetLayerPosition()?.Y ?? PageY);

    /// <summary>
    /// Whether the event has no position: a click fired by the user agent (<c>click()</c>, the keyboard activation), whose
    /// coordinates are 0, as in the browsers.
    /// </summary>
    internal bool IsPositionless { get; init; }

    /// <summary>Whether the event is a compatibility mouse event of a tap on a touch screen (https://w3c.github.io/pointerevents/#mapping-for-devices-that-do-not-support-hover).</summary>
    internal bool IsFromTouch { get; init; }

    /// <summary>The press on a scrollbar that a <c>mousedown</c> event of the user is (the scrollbar handles it: it has no other default action than the focus), or null.</summary>
    internal UserInput.ScrollbarPress? ScrollbarPress { get; init; }

    /// <summary>Gets a value indicating whether the Control key was active.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-mouseevent-ctrlkey"/>
    [IdlMember("ctrlKey", "https://w3c.github.io/uievents/#dom-mouseevent-ctrlkey")]
    public bool CtrlKey => _modifiers.Ctrl;

    /// <summary>Gets a value indicating whether the Shift key was active.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-mouseevent-shiftkey"/>
    [IdlMember("shiftKey", "https://w3c.github.io/uievents/#dom-mouseevent-shiftkey")]
    public bool ShiftKey => _modifiers.Shift;

    /// <summary>Gets a value indicating whether the Alt key was active.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-mouseevent-altkey"/>
    [IdlMember("altKey", "https://w3c.github.io/uievents/#dom-mouseevent-altkey")]
    public bool AltKey => _modifiers.Alt;

    /// <summary>Gets a value indicating whether the Meta key was active.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-mouseevent-metakey"/>
    [IdlMember("metaKey", "https://w3c.github.io/uievents/#dom-mouseevent-metakey")]
    public bool MetaKey => _modifiers.Meta;

    /// <summary>
    /// Gets the button that changed state: 0 for the main button, 1 for the auxiliary button, 2 for the secondary
    /// button.
    /// </summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-mouseevent-button"/>
    [IdlMember("button", "https://w3c.github.io/uievents/#dom-mouseevent-button")]
    public short Button { get; private set; }

    /// <summary>Gets the bit mask of the buttons that are pressed.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-mouseevent-buttons"/>
    [IdlMember("buttons", "https://w3c.github.io/uievents/#dom-mouseevent-buttons")]
    public ushort Buttons { get; private set; }

    /// <summary>Gets the secondary target of the event (for example the element the pointer left), or null.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-mouseevent-relatedtarget"/>
    [IdlMember("relatedTarget", "https://w3c.github.io/uievents/#dom-mouseevent-relatedtarget")]
    public new EventTarget? RelatedTarget => base.RelatedTarget;

    /// <summary>Gets the horizontal distance of the pointer movement since the previous event.</summary>
    /// <seealso href="https://w3c.github.io/pointerlock/#dom-mouseevent-movementx"/>
    [IdlMember("movementX", "https://w3c.github.io/pointerlock/#dom-mouseevent-movementx")]
    public double MovementX { get; private set; }

    /// <summary>Gets the vertical distance of the pointer movement since the previous event.</summary>
    /// <seealso href="https://w3c.github.io/pointerlock/#dom-mouseevent-movementy"/>
    [IdlMember("movementY", "https://w3c.github.io/pointerlock/#dom-mouseevent-movementy")]
    public double MovementY { get; private set; }

    /// <summary>
    /// Gets the horizontal coordinate of the pointer relative to the initial containing block (https://drafts.csswg.org/cssom-view/#dom-mouseevent-pagex):
    /// <see cref="ClientX"/> plus the horizontal scroll position of the window of the event (the window of the realm it was
    /// created in, else <see cref="UIEvent.View"/>).
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-mouseevent-pagex"/>
    [IdlMember("pageX", "https://drafts.csswg.org/cssom-view/#dom-mouseevent-pagex")]
    public double PageX => IsPositionless ? 0 : ClientX + (AssociatedWindow?.ScrollX ?? 0);

    /// <summary>Gets the vertical coordinate of the pointer relative to the initial containing block (https://drafts.csswg.org/cssom-view/#dom-mouseevent-pagey): see <see cref="PageX"/>.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-mouseevent-pagey"/>
    [IdlMember("pageY", "https://drafts.csswg.org/cssom-view/#dom-mouseevent-pagey")]
    public double PageY => IsPositionless ? 0 : ClientY + (AssociatedWindow?.ScrollY ?? 0);

    private Window? AssociatedWindow => _realmWindow ?? View as Window;

    /// <summary>
    /// Gets the horizontal coordinate of the pointer relative to the viewport (an alias of <see cref="ClientX"/>).
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-mouseevent-x"/>
    [IdlMember("x", "https://drafts.csswg.org/cssom-view/#dom-mouseevent-x")]
    public double X => ClientX;

    /// <summary>
    /// Gets the vertical coordinate of the pointer relative to the viewport (an alias of <see cref="ClientY"/>).
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-mouseevent-y"/>
    [IdlMember("y", "https://drafts.csswg.org/cssom-view/#dom-mouseevent-y")]
    public double Y => ClientY;

    /// <summary>
    /// Gets the horizontal coordinate of the pointer relative to the padding edge of the target while the event is dispatched
    /// (https://drafts.csswg.org/cssom-view/#dom-mouseevent-offsetx; the outermost svg element for an element of an SVG document
    /// fragment, as in the browsers), or <see cref="PageX"/>.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-mouseevent-offsetx"/>
    [IdlMember("offsetX", "https://drafts.csswg.org/cssom-view/#dom-mouseevent-offsetx")]
    public double OffsetX => GetOffsetPosition() is { } offset ? offset.X : PageX;

    /// <summary>Gets the vertical coordinate of the pointer relative to the padding edge of the target (https://drafts.csswg.org/cssom-view/#dom-mouseevent-offsety): see <see cref="OffsetX"/>.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-mouseevent-offsety"/>
    [IdlMember("offsetY", "https://drafts.csswg.org/cssom-view/#dom-mouseevent-offsety")]
    public double OffsetY => GetOffsetPosition() is { } offset ? offset.Y : PageY;

    /// <summary>
    /// The coordinates relative to the padding edge of the target, computed while the event is dispatched and kept afterwards
    /// (as in the browsers); rounded to integers, except for the pointer events with fractional coordinates
    /// (https://w3c.github.io/pointerevents/#event-coordinates: not click, auxclick and contextmenu).
    /// </summary>
    private (double X, double Y)? GetOffsetPosition()
    {
        if (IsPositionless)
            return (0, 0);

        if (_offset is null && DispatchFlag && Target is Element target && MouseEventGeometry.GetOffsetPosition(this, target) is { } offset)
        {
            _offset = HasFractionalCoordinates(Type) ? offset : (Math.Round(offset.X, MidpointRounding.AwayFromZero), Math.Round(offset.Y, MidpointRounding.AwayFromZero));
        }

        return _offset;
    }

    private (double X, double Y)? GetLayerPosition()
    {
        if (IsPositionless)
            return (0, 0);

        if (_layer is null && DispatchFlag && Target is Node target)
        {
            _layer = MouseEventGeometry.GetLayerPosition(this, target);
        }

        return _layer;
    }

    /// <summary>
    /// Returns whether the given modifier key (such as <c>Shift</c> or <c>CapsLock</c>) was active when the event
    /// occurred.
    /// </summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-mouseevent-getmodifierstate"/>
    [IdlMember("getModifierState", "https://w3c.github.io/uievents/#dom-mouseevent-getmodifierstate")]
    public bool GetModifierState(string keyArg)
    {
        ArgumentNullException.ThrowIfNull(keyArg);
        return _modifiers.Get(keyArg);
    }

    /// <summary>https://w3c.github.io/uievents/#dom-mouseevent-initmouseevent</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-mouseevent-initmouseevent"/>
    [IdlMember("initMouseEvent", "https://w3c.github.io/uievents/#dom-mouseevent-initmouseevent")]
    public void InitMouseEvent(string typeArg, bool bubblesArg = false, bool cancelableArg = false, EventTarget? viewArg = null, int detailArg = 0, int screenXArg = 0, int screenYArg = 0, int clientXArg = 0, int clientYArg = 0, bool ctrlKeyArg = false, bool altKeyArg = false, bool shiftKeyArg = false, bool metaKeyArg = false, short buttonArg = 0, EventTarget? relatedTargetArg = null)
    {
        ArgumentNullException.ThrowIfNull(typeArg);
        if (DispatchFlag)
            return;

        Initialize(typeArg, bubblesArg, cancelableArg);
        View = viewArg;
        Detail = detailArg;
        ScreenX = screenXArg;
        ScreenY = screenYArg;
        ClientX = clientXArg;
        ClientY = clientYArg;
        _modifiers.Ctrl = ctrlKeyArg;
        _modifiers.Alt = altKeyArg;
        _modifiers.Shift = shiftKeyArg;
        _modifiers.Meta = metaKeyArg;
        Button = buttonArg;
        base.RelatedTarget = relatedTargetArg;
    }
}

/// <summary>https://w3c.github.io/uievents/#dictdef-wheeleventinit</summary>
/// <seealso href="https://w3c.github.io/uievents/#dictdef-wheeleventinit"/>
[IdlInterface("WheelEventInit", "https://w3c.github.io/uievents/#dictdef-wheeleventinit")]
public sealed class WheelEventInit : MouseEventInit
{
    /// <summary>Gets the horizontal scroll amount.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-wheeleventinit-deltax"/>
    [IdlMember("deltaX", "https://w3c.github.io/uievents/#dom-wheeleventinit-deltax")]
    public double DeltaX { get; init; }

    /// <summary>Gets the vertical scroll amount.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-wheeleventinit-deltay"/>
    [IdlMember("deltaY", "https://w3c.github.io/uievents/#dom-wheeleventinit-deltay")]
    public double DeltaY { get; init; }

    /// <summary>Gets the scroll amount along the Z axis.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-wheeleventinit-deltaz"/>
    [IdlMember("deltaZ", "https://w3c.github.io/uievents/#dom-wheeleventinit-deltaz")]
    public double DeltaZ { get; init; }

    /// <summary>Gets the unit of the delta values: pixels, lines or pages.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-wheeleventinit-deltamode"/>
    [IdlMember("deltaMode", "https://w3c.github.io/uievents/#dom-wheeleventinit-deltamode")]
    public uint DeltaMode { get; init; }

    /// <summary>Gets a value indicating whether the event is produced by the momentum of a previous gesture.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-wheeleventinit-momentum"/>
    [IdlMember("momentum", "https://w3c.github.io/uievents/#dom-wheeleventinit-momentum")]
    public bool Momentum { get; init; }
}

/// <summary>https://w3c.github.io/uievents/#wheelevent</summary>
/// <seealso href="https://w3c.github.io/uievents/#wheelevent"/>
[IdlInterface("WheelEvent", "https://w3c.github.io/uievents/#wheelevent")]
public sealed class WheelEvent : MouseEvent
{
    /// <summary>The delta values are expressed in pixels.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-wheelevent-dom_delta_pixel"/>
    [IdlMember("DOM_DELTA_PIXEL", "https://w3c.github.io/uievents/#dom-wheelevent-dom_delta_pixel")]
    public const uint DomDeltaPixel = 0x00;

    /// <summary>The delta values are expressed in lines.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-wheelevent-dom_delta_line"/>
    [IdlMember("DOM_DELTA_LINE", "https://w3c.github.io/uievents/#dom-wheelevent-dom_delta_line")]
    public const uint DomDeltaLine = 0x01;

    /// <summary>The delta values are expressed in pages.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-wheelevent-dom_delta_page"/>
    [IdlMember("DOM_DELTA_PAGE", "https://w3c.github.io/uievents/#dom-wheelevent-dom_delta_page")]
    public const uint DomDeltaPage = 0x02;

    /// <summary>Creates a wheel event of the given type with the given initialization dictionary.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-wheelevent-wheelevent"/>
    [IdlMember("constructor", "https://w3c.github.io/uievents/#dom-wheelevent-wheelevent")]
    public WheelEvent(string type, WheelEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        DeltaX = eventInitDict?.DeltaX ?? 0;
        DeltaY = eventInitDict?.DeltaY ?? 0;
        DeltaZ = eventInitDict?.DeltaZ ?? 0;
        DeltaMode = eventInitDict?.DeltaMode ?? 0;
        Momentum = eventInitDict?.Momentum ?? false;
    }

    /// <summary>Gets the horizontal scroll amount.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-wheelevent-deltax"/>
    [IdlMember("deltaX", "https://w3c.github.io/uievents/#dom-wheelevent-deltax")]
    public double DeltaX { get; }

    /// <summary>Gets the vertical scroll amount.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-wheelevent-deltay"/>
    [IdlMember("deltaY", "https://w3c.github.io/uievents/#dom-wheelevent-deltay")]
    public double DeltaY { get; }

    /// <summary>Gets the scroll amount along the Z axis.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-wheelevent-deltaz"/>
    [IdlMember("deltaZ", "https://w3c.github.io/uievents/#dom-wheelevent-deltaz")]
    public double DeltaZ { get; }

    /// <summary>
    /// Gets the unit of the delta values: <see cref="DomDeltaPixel"/>, <see cref="DomDeltaLine"/> or <see
    /// cref="DomDeltaPage"/>.
    /// </summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-wheelevent-deltamode"/>
    [IdlMember("deltaMode", "https://w3c.github.io/uievents/#dom-wheelevent-deltamode")]
    public uint DeltaMode { get; }

    /// <summary>Gets a value indicating whether the event is produced by the momentum of a previous gesture.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-wheelevent-momentum"/>
    [IdlMember("momentum", "https://w3c.github.io/uievents/#dom-wheelevent-momentum")]
    public bool Momentum { get; }
}

/// <summary>https://w3c.github.io/uievents/#dictdef-keyboardeventinit</summary>
/// <seealso href="https://w3c.github.io/uievents/#dictdef-keyboardeventinit"/>
[IdlInterface("KeyboardEventInit", "https://w3c.github.io/uievents/#dictdef-keyboardeventinit")]
public sealed class KeyboardEventInit : EventModifierInit
{
    /// <summary>Gets the value of the key, such as <c>a</c> or <c>Enter</c>.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-keyboardeventinit-key"/>
    [IdlMember("key", "https://w3c.github.io/uievents/#dom-keyboardeventinit-key")]
    public string Key { get; init; } = "";

    /// <summary>Gets the physical key code, such as <c>KeyA</c> or <c>Enter</c>.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-keyboardeventinit-code"/>
    [IdlMember("code", "https://w3c.github.io/uievents/#dom-keyboardeventinit-code")]
    public string Code { get; init; } = "";

    /// <summary>Gets the location of the key on the device (standard, left, right or numpad).</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-keyboardeventinit-location"/>
    [IdlMember("location", "https://w3c.github.io/uievents/#dom-keyboardeventinit-location")]
    public uint Location { get; init; }

    /// <summary>Gets a value indicating whether the key is held down so that the event is auto-repeated.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-keyboardeventinit-repeat"/>
    [IdlMember("repeat", "https://w3c.github.io/uievents/#dom-keyboardeventinit-repeat")]
    public bool Repeat { get; init; }

    /// <summary>Gets a value indicating whether the event is part of a composition session.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-keyboardeventinit-iscomposing"/>
    [IdlMember("isComposing", "https://w3c.github.io/uievents/#dom-keyboardeventinit-iscomposing")]
    public bool IsComposing { get; init; }

    /// <summary>Gets the legacy Unicode code of the character key.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-keyboardeventinit-charcode"/>
    [IdlMember("charCode", "https://w3c.github.io/uievents/#dom-keyboardeventinit-charcode")]
    public uint CharCode { get; init; }

    /// <summary>Gets the legacy code of the key.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-keyboardeventinit-keycode"/>
    [IdlMember("keyCode", "https://w3c.github.io/uievents/#dom-keyboardeventinit-keycode")]
    public uint KeyCode { get; init; }
}

/// <summary>https://w3c.github.io/uievents/#keyboardevent</summary>
/// <seealso href="https://w3c.github.io/uievents/#keyboardevent"/>
[IdlInterface("KeyboardEvent", "https://w3c.github.io/uievents/#keyboardevent")]
public sealed class KeyboardEvent : UIEvent
{
    /// <summary>The key has a single location on the keyboard.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-keyboardevent-dom_key_location_standard"/>
    [IdlMember("DOM_KEY_LOCATION_STANDARD", "https://w3c.github.io/uievents/#dom-keyboardevent-dom_key_location_standard")]
    public const uint DomKeyLocationStandard = 0x00;

    /// <summary>The key is on the left side of the keyboard.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-keyboardevent-dom_key_location_left"/>
    [IdlMember("DOM_KEY_LOCATION_LEFT", "https://w3c.github.io/uievents/#dom-keyboardevent-dom_key_location_left")]
    public const uint DomKeyLocationLeft = 0x01;

    /// <summary>The key is on the right side of the keyboard.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-keyboardevent-dom_key_location_right"/>
    [IdlMember("DOM_KEY_LOCATION_RIGHT", "https://w3c.github.io/uievents/#dom-keyboardevent-dom_key_location_right")]
    public const uint DomKeyLocationRight = 0x02;

    /// <summary>The key is on the numeric keypad.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-keyboardevent-dom_key_location_numpad"/>
    [IdlMember("DOM_KEY_LOCATION_NUMPAD", "https://w3c.github.io/uievents/#dom-keyboardevent-dom_key_location_numpad")]
    public const uint DomKeyLocationNumpad = 0x03;

    private ModifierState _modifiers;

    /// <summary>Creates a keyboard event of the given type with the given initialization dictionary.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-keyboardevent-keyboardevent"/>
    [IdlMember("constructor", "https://w3c.github.io/uievents/#dom-keyboardevent-keyboardevent")]
    public KeyboardEvent(string type, KeyboardEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        _modifiers = ModifierState.From(eventInitDict);
        if (eventInitDict is not null)
        {
            Key = eventInitDict.Key;
            Code = eventInitDict.Code;
            Location = eventInitDict.Location;
            Repeat = eventInitDict.Repeat;
            IsComposing = eventInitDict.IsComposing;
            CharCode = eventInitDict.CharCode;
            KeyCode = eventInitDict.KeyCode;
        }
    }

    private KeyboardEvent()
    {
    }

    internal static KeyboardEvent CreateUninitializedKeyboardEvent() => new();

    /// <summary>Gets the value of the key, such as <c>a</c> or <c>Enter</c>.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-keyboardevent-key"/>
    [IdlMember("key", "https://w3c.github.io/uievents/#dom-keyboardevent-key")]
    public string Key { get; private set; } = "";

    /// <summary>Gets the physical key code, such as <c>KeyA</c> or <c>Enter</c>.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-keyboardevent-code"/>
    [IdlMember("code", "https://w3c.github.io/uievents/#dom-keyboardevent-code")]
    public string Code { get; private set; } = "";

    /// <summary>Gets the location of the key on the device (standard, left, right or numpad).</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-keyboardevent-location"/>
    [IdlMember("location", "https://w3c.github.io/uievents/#dom-keyboardevent-location")]
    public uint Location { get; private set; }

    /// <summary>Gets a value indicating whether the Control key was active.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-keyboardevent-ctrlkey"/>
    [IdlMember("ctrlKey", "https://w3c.github.io/uievents/#dom-keyboardevent-ctrlkey")]
    public bool CtrlKey => _modifiers.Ctrl;

    /// <summary>Gets a value indicating whether the Shift key was active.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-keyboardevent-shiftkey"/>
    [IdlMember("shiftKey", "https://w3c.github.io/uievents/#dom-keyboardevent-shiftkey")]
    public bool ShiftKey => _modifiers.Shift;

    /// <summary>Gets a value indicating whether the Alt key was active.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-keyboardevent-altkey"/>
    [IdlMember("altKey", "https://w3c.github.io/uievents/#dom-keyboardevent-altkey")]
    public bool AltKey => _modifiers.Alt;

    /// <summary>Gets a value indicating whether the Meta key was active.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-keyboardevent-metakey"/>
    [IdlMember("metaKey", "https://w3c.github.io/uievents/#dom-keyboardevent-metakey")]
    public bool MetaKey => _modifiers.Meta;

    /// <summary>Gets a value indicating whether the key is held down so that the event is auto-repeated.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-keyboardevent-repeat"/>
    [IdlMember("repeat", "https://w3c.github.io/uievents/#dom-keyboardevent-repeat")]
    public bool Repeat { get; }

    /// <summary>Gets a value indicating whether the event is part of a composition session.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-keyboardevent-iscomposing"/>
    [IdlMember("isComposing", "https://w3c.github.io/uievents/#dom-keyboardevent-iscomposing")]
    public bool IsComposing { get; }

    /// <summary>Gets the legacy Unicode code of the character key.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-keyboardevent-charcode"/>
    [IdlMember("charCode", "https://w3c.github.io/uievents/#dom-keyboardevent-charcode")]
    public uint CharCode { get; }

    /// <summary>Gets the legacy code of the key.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-keyboardevent-keycode"/>
    [IdlMember("keyCode", "https://w3c.github.io/uievents/#dom-keyboardevent-keycode")]
    public uint KeyCode { get; }

    /// <summary>
    /// Returns whether the given modifier key (such as <c>Shift</c> or <c>CapsLock</c>) was active when the event
    /// occurred.
    /// </summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-keyboardevent-getmodifierstate"/>
    [IdlMember("getModifierState", "https://w3c.github.io/uievents/#dom-keyboardevent-getmodifierstate")]
    public bool GetModifierState(string keyArg)
    {
        ArgumentNullException.ThrowIfNull(keyArg);
        return _modifiers.Get(keyArg);
    }

    /// <summary>https://w3c.github.io/uievents/#dom-keyboardevent-initkeyboardevent</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-keyboardevent-initkeyboardevent"/>
    [IdlMember("initKeyboardEvent", "https://w3c.github.io/uievents/#dom-keyboardevent-initkeyboardevent")]
    public void InitKeyboardEvent(string typeArg, bool bubblesArg = false, bool cancelableArg = false, EventTarget? viewArg = null, string keyArg = "", uint locationArg = 0, bool ctrlKey = false, bool altKey = false, bool shiftKey = false, bool metaKey = false)
    {
        ArgumentNullException.ThrowIfNull(typeArg);
        if (DispatchFlag)
            return;

        Initialize(typeArg, bubblesArg, cancelableArg);
        View = viewArg;
        Key = keyArg ?? "";
        Location = locationArg;
        _modifiers.Ctrl = ctrlKey;
        _modifiers.Alt = altKey;
        _modifiers.Shift = shiftKey;
        _modifiers.Meta = metaKey;
    }
}

/// <summary>https://w3c.github.io/uievents/#dictdef-compositioneventinit</summary>
/// <seealso href="https://w3c.github.io/uievents/#dictdef-compositioneventinit"/>
[IdlInterface("CompositionEventInit", "https://w3c.github.io/uievents/#dictdef-compositioneventinit")]
public sealed class CompositionEventInit : UIEventInit
{
    /// <summary>Gets the characters generated by the input method during the composition.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-compositioneventinit-data"/>
    [IdlMember("data", "https://w3c.github.io/uievents/#dom-compositioneventinit-data")]
    public string Data { get; init; } = "";
}

/// <summary>https://w3c.github.io/uievents/#compositionevent</summary>
/// <seealso href="https://w3c.github.io/uievents/#compositionevent"/>
[IdlInterface("CompositionEvent", "https://w3c.github.io/uievents/#compositionevent")]
public sealed class CompositionEvent : UIEvent
{
    /// <summary>Creates a composition event of the given type with the given initialization dictionary.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-compositionevent-compositionevent"/>
    [IdlMember("constructor", "https://w3c.github.io/uievents/#dom-compositionevent-compositionevent")]
    public CompositionEvent(string type, CompositionEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        Data = eventInitDict?.Data ?? "";
    }

    private CompositionEvent()
    {
    }

    internal static CompositionEvent CreateUninitializedCompositionEvent() => new();

    /// <summary>Gets the characters generated by the input method during the composition.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-compositionevent-data"/>
    [IdlMember("data", "https://w3c.github.io/uievents/#dom-compositionevent-data")]
    public string Data { get; private set; } = "";

    /// <summary>https://w3c.github.io/uievents/#dom-compositionevent-initcompositionevent</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-compositionevent-initcompositionevent"/>
    [IdlMember("initCompositionEvent", "https://w3c.github.io/uievents/#dom-compositionevent-initcompositionevent")]
    public void InitCompositionEvent(string typeArg, bool bubblesArg = false, bool cancelableArg = false, EventTarget? viewArg = null, string dataArg = "")
    {
        ArgumentNullException.ThrowIfNull(typeArg);
        if (DispatchFlag)
            return;

        Initialize(typeArg, bubblesArg, cancelableArg);
        View = viewArg;
        Data = dataArg ?? "";
    }
}

/// <summary>https://w3c.github.io/uievents/#dictdef-inputeventinit</summary>
/// <seealso href="https://w3c.github.io/uievents/#dictdef-inputeventinit"/>
[IdlInterface("InputEventInit", "https://w3c.github.io/uievents/#dictdef-inputeventinit")]
public sealed class InputEventInit : UIEventInit
{
    /// <summary>Gets the inserted characters, or null when the input does not insert text.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-inputeventinit-data"/>
    [IdlMember("data", "https://w3c.github.io/uievents/#dom-inputeventinit-data")]
    public string? Data { get; init; }

    /// <summary>Gets a value indicating whether the event is part of a composition session.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-inputeventinit-iscomposing"/>
    [IdlMember("isComposing", "https://w3c.github.io/uievents/#dom-inputeventinit-iscomposing")]
    public bool IsComposing { get; init; }

    /// <summary>Gets the type of the change, such as <c>insertText</c> or <c>deleteContentBackward</c>.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-inputeventinit-inputtype"/>
    [IdlMember("inputType", "https://w3c.github.io/uievents/#dom-inputeventinit-inputtype")]
    public string InputType { get; init; } = "";

    /// <summary>Gets the data of a paste or of a drop in editable content (rich text), or null.</summary>
    /// <seealso href="https://w3c.github.io/input-events/#dom-inputeventinit-datatransfer"/>
    [IdlMember("dataTransfer", "https://w3c.github.io/input-events/#dom-inputeventinit-datatransfer")]
    public DataTransfer? DataTransfer { get; init; }

    /// <summary>Gets the ranges that the editing operation affects.</summary>
    /// <seealso href="https://w3c.github.io/input-events/#dom-inputeventinit-targetranges"/>
    [IdlMember("targetRanges", "https://w3c.github.io/input-events/#dom-inputeventinit-targetranges")]
    public IReadOnlyList<StaticRange> TargetRanges { get; init; } = [];
}

/// <summary>https://w3c.github.io/uievents/#inputevent</summary>
/// <seealso href="https://w3c.github.io/uievents/#inputevent"/>
[IdlInterface("InputEvent", "https://w3c.github.io/uievents/#inputevent")]
public sealed class InputEvent : UIEvent
{
    private IReadOnlyList<StaticRange> _targetRanges = [];

    /// <summary>Creates an input event of the given type with the given initialization dictionary.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-inputevent-inputevent"/>
    [IdlMember("constructor", "https://w3c.github.io/uievents/#dom-inputevent-inputevent")]
    public InputEvent(string type, InputEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        Data = eventInitDict?.Data;
        IsComposing = eventInitDict?.IsComposing ?? false;
        InputType = eventInitDict?.InputType ?? "";
        DataTransfer = eventInitDict?.DataTransfer;
        _targetRanges = eventInitDict?.TargetRanges ?? [];
    }

    /// <summary>Gets the inserted characters, or null when the input does not insert text.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-inputevent-data"/>
    [IdlMember("data", "https://w3c.github.io/uievents/#dom-inputevent-data")]
    public string? Data { get; }

    /// <summary>Gets a value indicating whether the event is part of a composition session.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-inputevent-iscomposing"/>
    [IdlMember("isComposing", "https://w3c.github.io/uievents/#dom-inputevent-iscomposing")]
    public bool IsComposing { get; }

    /// <summary>Gets the type of the change, such as <c>insertText</c> or <c>deleteContentBackward</c>.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-inputevent-inputtype"/>
    [IdlMember("inputType", "https://w3c.github.io/uievents/#dom-inputevent-inputtype")]
    public string InputType { get; }

    /// <summary>
    /// Gets the data of a paste or of a drop in editable content (the rich text and the plain text), or null: the plain text
    /// inserted in a text control is in <see cref="Data"/> (https://w3c.github.io/input-events/#dom-inputevent-datatransfer).
    /// </summary>
    /// <seealso href="https://w3c.github.io/input-events/#dom-inputevent-datatransfer"/>
    [IdlMember("dataTransfer", "https://w3c.github.io/input-events/#dom-inputevent-datatransfer")]
    public DataTransfer? DataTransfer { get; }

    /// <summary>Returns the ranges that the editing operation affects.</summary>
    /// <seealso href="https://w3c.github.io/input-events/#dom-inputevent-gettargetranges"/>
    [IdlMember("getTargetRanges", "https://w3c.github.io/input-events/#dom-inputevent-gettargetranges")]
    public IReadOnlyList<StaticRange> GetTargetRanges() => _targetRanges;

    /// <summary>The target ranges of the event of a user edit are no longer available once the event is dispatched (as in the browsers).</summary>
    internal void ClearTargetRanges() => _targetRanges = [];
}

/// <summary>https://w3c.github.io/uievents/#textevent</summary>
/// <seealso href="https://w3c.github.io/uievents/#textevent"/>
[IdlInterface("TextEvent", "https://w3c.github.io/uievents/#textevent")]
public sealed class TextEvent : UIEvent
{
    private TextEvent()
    {
    }

    internal static TextEvent CreateUninitializedTextEvent() => new();

    /// <summary>Gets the characters of the text input.</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-textevent-data"/>
    [IdlMember("data", "https://w3c.github.io/uievents/#dom-textevent-data")]
    public string Data { get; private set; } = "";

    /// <summary>https://w3c.github.io/uievents/#dom-textevent-inittextevent</summary>
    /// <seealso href="https://w3c.github.io/uievents/#dom-textevent-inittextevent"/>
    [IdlMember("initTextEvent", "https://w3c.github.io/uievents/#dom-textevent-inittextevent")]
    public void InitTextEvent(string type, bool bubbles = false, bool cancelable = false, EventTarget? view = null, string data = "undefined")
    {
        ArgumentNullException.ThrowIfNull(type);
        if (DispatchFlag)
            return;

        Initialize(type, bubbles, cancelable);
        View = view;
        Data = data ?? "undefined";
    }
}

/// <summary>https://html.spec.whatwg.org/multipage/dnd.html#drageventinit</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/dnd.html#drageventinit"/>
[IdlInterface("DragEventInit", "https://html.spec.whatwg.org/multipage/dnd.html#drageventinit")]
public sealed class DragEventInit : MouseEventInit
{
    /// <summary>Gets the data transfer object of the drag operation, or null.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dnd.html#dom-drageventinit-datatransfer"/>
    [IdlMember("dataTransfer", "https://html.spec.whatwg.org/multipage/dnd.html#dom-drageventinit-datatransfer")]
    public DataTransfer? DataTransfer { get; init; }
}

/// <summary>https://html.spec.whatwg.org/multipage/dnd.html#dragevent</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/dnd.html#dragevent"/>
[IdlInterface("DragEvent", "https://html.spec.whatwg.org/multipage/dnd.html#dragevent")]
public sealed class DragEvent : MouseEvent
{
    /// <summary>Creates a drag event of the given type with the given initialization dictionary.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dnd.html#dom-dragevent-dragevent"/>
    [IdlMember("constructor", "https://html.spec.whatwg.org/multipage/dnd.html#dom-dragevent-dragevent")]
    public DragEvent(string type, DragEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        DataTransfer = eventInitDict?.DataTransfer;
    }

    private DragEvent()
    {
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dnd.html#dom-dragevent-datatransfer</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dnd.html#dom-dragevent-datatransfer"/>
    [IdlMember("dataTransfer", "https://html.spec.whatwg.org/multipage/dnd.html#dom-dragevent-datatransfer")]
    public DataTransfer? DataTransfer { get; }

    internal static DragEvent CreateUninitializedDragEvent() => new();
}
