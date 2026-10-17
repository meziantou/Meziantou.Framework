namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A change of the visibility state of a document (https://html.spec.whatwg.org/multipage/interaction.html#the-visibilitystateentry-interface),
/// reported to the <c>visibility-state</c> performance observers of its window and available from its performance timeline:
/// its name is the visibility state (<c>visible</c> or <c>hidden</c>) and its start time is the time of the change (0 for the
/// initial visibility state of the document).
/// </summary>
[IdlInterface("VisibilityStateEntry", "https://html.spec.whatwg.org/multipage/interaction.html#visibilitystateentry")]
public sealed class VisibilityStateEntry : PerformanceEntry
{
    /// <summary>The entry type of the visibility state entries.</summary>
    internal const string VisibilityStateEntryType = "visibility-state";

    /// <summary>The maximum size of the performance entry buffer of the visibility states (https://w3c.github.io/timing-entrytypes-registry/#registry).</summary>
    internal const int MaxBufferSize = 50;

    internal VisibilityStateEntry(GlobalScope global, bool visible, double timestamp)
        : base(global, VisibilityStateEntryType, visible ? "visible" : "hidden", timestamp, duration: 0)
    {
    }

    /// <summary>Gets the visibility state: <c>visible</c> or <c>hidden</c> (https://html.spec.whatwg.org/multipage/interaction.html#dom-visibilitystateentry-name).</summary>
    [IdlMember("name", "https://html.spec.whatwg.org/multipage/interaction.html#dom-visibilitystateentry-name")]
    public new string Name => base.Name;

    /// <summary>Gets <c>visibility-state</c> (https://html.spec.whatwg.org/multipage/interaction.html#dom-visibilitystateentry-entrytype).</summary>
    [IdlMember("entryType", "https://html.spec.whatwg.org/multipage/interaction.html#dom-visibilitystateentry-entrytype")]
    public new string EntryType => base.EntryType;

    /// <summary>Gets the time of the change of the visibility state (https://html.spec.whatwg.org/multipage/interaction.html#dom-visibilitystateentry-starttime).</summary>
    [IdlMember("startTime", "https://html.spec.whatwg.org/multipage/interaction.html#dom-visibilitystateentry-starttime")]
    public new double StartTime => base.StartTime;

    /// <summary>Gets 0 (https://html.spec.whatwg.org/multipage/interaction.html#dom-visibilitystateentry-duration).</summary>
    [IdlMember("duration", "https://html.spec.whatwg.org/multipage/interaction.html#dom-visibilitystateentry-duration")]
    public new uint Duration => 0;
}
