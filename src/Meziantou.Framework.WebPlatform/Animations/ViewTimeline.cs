using System.Collections;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.TypedOm;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Animations;

/// <summary>The options of a view timeline (https://drafts.csswg.org/scroll-animations-1/#dictdef-viewtimelineoptions).</summary>
/// <seealso href="https://drafts.csswg.org/scroll-animations-1/#dictdef-viewtimelineoptions"/>
[IdlInterface("ViewTimelineOptions", "https://drafts.csswg.org/scroll-animations-1/#dictdef-viewtimelineoptions")]
public sealed class ViewTimelineOptions
{
    /// <summary>Gets the element whose visibility in the scrollport of its nearest ancestor scroll container drives the timeline.</summary>
    /// <seealso href="https://drafts.csswg.org/scroll-animations-1/#dom-viewtimelineoptions-subject"/>
    [IdlMember("subject", "https://drafts.csswg.org/scroll-animations-1/#dom-viewtimelineoptions-subject")]
    public Element Subject { get; init; } = null!;

    /// <summary>Gets the axis of the timeline.</summary>
    /// <seealso href="https://drafts.csswg.org/scroll-animations-1/#dom-viewtimelineoptions-axis"/>
    [IdlMember("axis", "https://drafts.csswg.org/scroll-animations-1/#dom-viewtimelineoptions-axis")]
    public ScrollAxis Axis { get; init; }

    /// <summary>
    /// Gets the insets of the view progress visibility range: a string parsed as a <c>view-timeline-inset</c> value (e.g.
    /// "auto", "10px 20%"), or a sequence of one or two <see cref="CssNumericValue"/> or <see cref="CssKeywordValue"/> ("auto").
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/scroll-animations-1/#dom-viewtimelineoptions-inset"/>
    [IdlMember("inset", "https://drafts.csswg.org/scroll-animations-1/#dom-viewtimelineoptions-inset")]
    public object? Inset { get; init; } = "auto";
}

/// <summary>
/// A view progress timeline (https://drafts.csswg.org/scroll-animations-1/#viewtimeline-interface): its current time is the
/// progress of the visibility of a subject in the scrollport of its nearest ancestor scroll container, as a percentage (0%
/// when it starts entering the scrollport, 100% when it has left it).
/// </summary>
/// <seealso href="https://drafts.csswg.org/scroll-animations-1/#viewtimeline"/>
[IdlInterface("ViewTimeline", "https://drafts.csswg.org/scroll-animations-1/#viewtimeline")]
public sealed class ViewTimeline : ScrollTimeline
{
    /// <summary>Creates a view timeline of a subject (https://drafts.csswg.org/scroll-animations-1/#dom-viewtimeline-viewtimeline): the timeline belongs to the document of the subject.</summary>
    /// <exception cref="TypeErrorException">The inset is invalid.</exception>
    /// <seealso href="https://drafts.csswg.org/scroll-animations-1/#dom-viewtimeline-viewtimeline"/>
    [IdlMember("constructor", "https://drafts.csswg.org/scroll-animations-1/#dom-viewtimeline-viewtimeline")]
    public ViewTimeline(ViewTimelineOptions options)
        : this((options ?? throw new ArgumentNullException(nameof(options))).Subject?.NodeDocument ?? throw new TypeErrorException("The subject of a view timeline is required."), options.Subject, options.Axis, ParseInset(options.Inset))
    {
        UpdateSnapshot();
    }

    private ViewTimeline(Document document, Element subject, ScrollAxis axis, (LengthPercentage? Start, LengthPercentage? End) inset, string? pseudoElement = null)
        : base(document, subject, axis, ScrollTimelineScroller.Nearest, pseudoElement)
    {
        Subject = subject;
        Inset = inset;
    }

    /// <summary>Creates the timeline of a <c>view()</c> function of <c>animation-timeline</c>, or of a named view progress timeline.</summary>
    internal static ViewTimeline Create(Element subject, string? pseudoElement, ScrollAxis axis, (LengthPercentage? Start, LengthPercentage? End) inset) => new(subject.NodeDocument, subject, axis, inset, pseudoElement);

    /// <summary>Gets the subject of the timeline (https://drafts.csswg.org/scroll-animations-1/#dom-viewtimeline-subject).</summary>
    /// <seealso href="https://drafts.csswg.org/scroll-animations-1/#dom-viewtimeline-subject"/>
    [IdlMember("subject", "https://drafts.csswg.org/scroll-animations-1/#dom-viewtimeline-subject")]
    public Element Subject { get; }

    /// <summary>The insets of the view progress visibility range in CSS px (null for auto: the scroll-padding of the scroll container).</summary>
    internal (LengthPercentage? Start, LengthPercentage? End) Inset { get; set; }

    /// <summary>
    /// Gets the nearest ancestor scroll container of the subject that is scrollable in the axis of the timeline
    /// (https://drafts.csswg.org/scroll-animations-1/#dom-scrolltimeline-source), or null when the subject does not generate a box.
    /// </summary>
    public override Element? Source => ScrollTimelineGeometry.FindNearestScrollContainer(Subject, PseudoElement, Axis).Element;

    /// <summary>
    /// Gets the scroll offset of the 0% position of the timeline, in px from the scroll origin
    /// (https://drafts.csswg.org/scroll-animations-1/#dom-viewtimeline-startoffset): 0px when the timeline is inactive.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/scroll-animations-1/#dom-viewtimeline-startoffset"/>
    [IdlMember("startOffset", "https://drafts.csswg.org/scroll-animations-1/#dom-viewtimeline-startoffset")]
    public CssNumericValue StartOffset => new CssUnitValue(Snapshot.IsActive ? Snapshot.StartOffset : 0, "px");

    /// <summary>
    /// Gets the scroll offset of the 100% position of the timeline, in px from the scroll origin
    /// (https://drafts.csswg.org/scroll-animations-1/#dom-viewtimeline-endoffset): 0px when the timeline is inactive.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/scroll-animations-1/#dom-viewtimeline-endoffset"/>
    [IdlMember("endOffset", "https://drafts.csswg.org/scroll-animations-1/#dom-viewtimeline-endoffset")]
    public CssNumericValue EndOffset => new CssUnitValue(Snapshot.IsActive ? Snapshot.EndOffset : 0, "px");

    private protected override ScrollTimelineSnapshot ComputeSnapshot()
    {
        if (!IsEnabled(Document))
            return ScrollTimelineSnapshot.Inactive;

        return ScrollTimelineGeometry.ComputeViewTimeline(Subject, PseudoElement, Axis, Inset.Start, Inset.End);
    }

    /// <summary>
    /// Parses the inset of the options (https://drafts.csswg.org/scroll-animations-1/#dom-viewtimeline-viewtimeline): a string is
    /// a <c>view-timeline-inset</c> value; a sequence has one or two values (the second one defaults to the first one), which
    /// are lengths, percentages or the keyword auto. The lengths must be absolute.
    /// </summary>
    private static (LengthPercentage? Start, LengthPercentage? End) ParseInset(object? inset)
    {
        switch (inset)
        {
            case null:
                return (null, null);
            case string text:
                return ParseInsetString(text) ?? throw new TypeErrorException("'" + text + "' is not a valid inset.");
            case IEnumerable values:
            {
                var items = new List<LengthPercentage?>(2);
                foreach (var value in values)
                {
                    LengthPercentage? item = value switch
                    {
                        CssKeywordValue keyword when InfraAscii.EqualsIgnoreAsciiCase(keyword.Value, "auto") => null,
                        CssNumericValue numeric => ParseInsetString(numeric.ToString()) is ({ } start, _) ? start : throw new TypeErrorException("'" + numeric + "' is not a valid inset."),
                        _ => throw new TypeErrorException("The inset must be a length, a percentage or auto."),
                    };

                    items.Add(item);
                }

                return items.Count switch
                {
                    1 => (items[0], items[0]),
                    2 => (items[0], items[1]),
                    _ => throw new TypeErrorException("The inset must have one or two values."),
                };
            }

            default:
                throw new TypeErrorException("The inset must be a string or a sequence.");
        }
    }

    /// <summary>Parses a single <c>view-timeline-inset</c> item whose lengths are absolute, or returns null.</summary>
    private static (LengthPercentage? Start, LengthPercentage? End)? ParseInsetString(string text)
    {
        var values = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(text));
        if (values.Count is 0 or > 2 || CssShorthands.ParseValue("view-timeline-inset", values, originalText: null) is not [{ ShorthandName: null }])
            return null;

        var start = ParseInsetValue(values[0], out var valid);
        if (!valid)
            return null;

        if (values.Count == 1)
            return (start, start);

        var end = ParseInsetValue(values[1], out valid);
        return valid ? (start, end) : null;
    }

    private static LengthPercentage? ParseInsetValue(CssComponentValue value, out bool valid)
    {
        valid = true;
        if (value is CssPreservedToken { Type: CssTokenType.Ident } ident && ident.Token.IsIdent("auto"))
            return null;

        if (TimelineRangeBoundary.TryParseAbsoluteOffset(value, out var offset))
            return offset;

        valid = false;
        return null;
    }

    /// <summary>
    /// Parses an item of the computed value of <c>view-timeline-inset</c> (or the inset of <c>view()</c>): one or two values,
    /// auto or a &lt;length-percentage&gt;; the second value defaults to the first one.
    /// </summary>
    internal static (LengthPercentage? Start, LengthPercentage? End) ParseComputedInset(List<CssComponentValue> values)
    {
        if (values.Count == 0)
            return (null, null);

        var start = ParseComputedInsetValue(values[0]);
        return values.Count == 1 ? (start, start) : (start, ParseComputedInsetValue(values[1]));
    }

    private static LengthPercentage? ParseComputedInsetValue(CssComponentValue value)
        => LengthPercentage.TryParse(value, zoom: 1, nonNegative: false, out var result) ? result : null;
}
