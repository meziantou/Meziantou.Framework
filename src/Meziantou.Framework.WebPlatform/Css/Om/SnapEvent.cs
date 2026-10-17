using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Om;

/// <summary>https://drafts.csswg.org/css-scroll-snap-2/#dictdef-snapeventinit</summary>
[IdlInterface("SnapEventInit", "https://drafts.csswg.org/css-scroll-snap-2/#dictdef-snapeventinit")]
public sealed class SnapEventInit : EventInit
{
    /// <summary>Gets the snap target in the block axis.</summary>
    [IdlMember("snapTargetBlock", "https://drafts.csswg.org/css-scroll-snap-2/#dom-snapeventinit-snaptargetblock")]
    public Node? SnapTargetBlock { get; init; }

    /// <summary>Gets the snap target in the inline axis.</summary>
    [IdlMember("snapTargetInline", "https://drafts.csswg.org/css-scroll-snap-2/#dom-snapeventinit-snaptargetinline")]
    public Node? SnapTargetInline { get; init; }
}

/// <summary>
/// The event fired at a scroll snap container (or at the document for the viewport) when the snap targets it snaps to change
/// (<c>scrollsnapchanging</c>, when a scroll operation starts) or changed (<c>scrollsnapchange</c>, when a scroll operation is
/// completed or after a layout change) (https://drafts.csswg.org/css-scroll-snap-2/#snap-events).
/// </summary>
[IdlInterface("SnapEvent", "https://drafts.csswg.org/css-scroll-snap-2/#snapevent")]
public sealed class SnapEvent : Event
{
    /// <summary>Creates an event with the given type and initialization dictionary.</summary>
    [IdlMember("constructor", "https://drafts.csswg.org/css-scroll-snap-2/#dom-snapevent-snapevent")]
    public SnapEvent(string type, SnapEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        SnapTargetBlock = eventInitDict?.SnapTargetBlock;
        SnapTargetInline = eventInitDict?.SnapTargetInline;
    }

    /// <summary>
    /// Gets the element the snap container is snapped to in the block axis (the element of a pseudo-element), or null when it is
    /// not snapped in that axis (https://drafts.csswg.org/css-scroll-snap-2/#dom-snapevent-snaptargetblock).
    /// </summary>
    [IdlMember("snapTargetBlock", "https://drafts.csswg.org/css-scroll-snap-2/#dom-snapevent-snaptargetblock")]
    public Node? SnapTargetBlock { get; }

    /// <summary>Gets the element the snap container is snapped to in the inline axis (https://drafts.csswg.org/css-scroll-snap-2/#dom-snapevent-snaptargetinline): see <see cref="SnapTargetBlock"/>.</summary>
    [IdlMember("snapTargetInline", "https://drafts.csswg.org/css-scroll-snap-2/#dom-snapevent-snaptargetinline")]
    public Node? SnapTargetInline { get; }
}
