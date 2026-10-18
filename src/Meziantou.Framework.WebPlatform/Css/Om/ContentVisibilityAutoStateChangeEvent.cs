using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Om;

/// <summary>https://drafts.csswg.org/css-contain-2/#dictdef-contentvisibilityautostatechangeeventinit</summary>
[IdlInterface("ContentVisibilityAutoStateChangeEventInit", "https://drafts.csswg.org/css-contain-2/#dictdef-contentvisibilityautostatechangeeventinit")]
public sealed class ContentVisibilityAutoStateChangeEventInit : EventInit
{
    /// <summary>Gets whether the element skips its contents.</summary>
    [IdlMember("skipped", "https://drafts.csswg.org/css-contain-2/#dom-contentvisibilityautostatechangeeventinit-skipped")]
    public bool Skipped { get; init; }
}

/// <summary>
/// The event fired at an element with <c>content-visibility: auto</c> when it starts or stops skipping its contents
/// (https://drafts.csswg.org/css-contain-2/#content-visibility-auto-state-change).
/// </summary>
[IdlInterface("ContentVisibilityAutoStateChangeEvent", "https://drafts.csswg.org/css-contain-2/#contentvisibilityautostatechangeevent")]
public sealed class ContentVisibilityAutoStateChangeEvent : Event
{
    /// <summary>Creates an event with the given type and initialization dictionary.</summary>
    [IdlMember("constructor", "https://drafts.csswg.org/css-contain-2/#dom-contentvisibilityautostatechangeevent-contentvisibilityautostatechangeevent")]
    public ContentVisibilityAutoStateChangeEvent(string type, ContentVisibilityAutoStateChangeEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        Skipped = eventInitDict?.Skipped ?? false;
    }

    /// <summary>Gets whether the target changed state to skip its contents (false when it stopped skipping them).</summary>
    [IdlMember("skipped", "https://drafts.csswg.org/css-contain-2/#dom-contentvisibilityautostatechangeevent-skipped")]
    public bool Skipped { get; }
}
