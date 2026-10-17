using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.TypedOm;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Animations;

/// <summary>A named timeline range (https://drafts.csswg.org/scroll-animations-1/#named-ranges), or none for the whole timeline.</summary>
internal enum TimelineRangeName
{
    None,
    Cover,
    Contain,
    Entry,
    Exit,
    EntryCrossing,
    ExitCrossing,
    Scroll,
}

/// <summary>
/// A boundary of an animation attachment range (https://drafts.csswg.org/scroll-animations-1/#named-range-animation-declaration):
/// a point of the timeline given by a named timeline range and an offset in it (the start or the end of the range when the
/// offset is omitted). The value <c>normal</c> is represented by null.
/// </summary>
internal sealed class TimelineRangeBoundary : IEquatable<TimelineRangeBoundary>
{
    private TimelineRangeBoundary(TimelineRangeName name, LengthPercentage offset, CssNumericValue offsetValue)
    {
        Name = name;
        Offset = offset;
        OffsetValue = offsetValue;
    }

    /// <summary>The named timeline range, or none for the whole timeline.</summary>
    public TimelineRangeName Name { get; }

    /// <summary>The offset in the range, in CSS px (unzoomed): the percentages refer to the size of the range.</summary>
    public LengthPercentage Offset { get; }

    /// <summary>The offset as a numeric value (the <c>offset</c> of <see cref="TimelineRangeOffset"/>).</summary>
    public CssNumericValue OffsetValue { get; }

    /// <summary>
    /// The effective zoom of the element whose style declares the boundary (<c>animation-range</c>): the lengths of the offset are
    /// multiplied by it, as the used values of the other lengths (https://drafts.csswg.org/css-viewport/#zoom-property). 1 for the
    /// boundaries set by scripts.
    /// </summary>
    public double Zoom { get; private init; } = 1;

    /// <summary>Returns the boundary whose lengths are scaled by the effective zoom of an element.</summary>
    public TimelineRangeBoundary WithZoom(double zoom) => zoom == Zoom || !(zoom > 0) ? this : new TimelineRangeBoundary(Name, Offset, OffsetValue) { Zoom = zoom };

    /// <summary>Returns the keyword of a named timeline range, or null for none.</summary>
    public static string? GetName(TimelineRangeName name) => name switch
    {
        TimelineRangeName.Cover => "cover",
        TimelineRangeName.Contain => "contain",
        TimelineRangeName.Entry => "entry",
        TimelineRangeName.Exit => "exit",
        TimelineRangeName.EntryCrossing => "entry-crossing",
        TimelineRangeName.ExitCrossing => "exit-crossing",
        TimelineRangeName.Scroll => "scroll",
        _ => null,
    };

    /// <summary>Parses a &lt;timeline-range-name&gt; (ASCII case-insensitive).</summary>
    public static bool TryParseName(string value, out TimelineRangeName name)
    {
        name = InfraAscii.ToAsciiLowercase(value) switch
        {
            "cover" => TimelineRangeName.Cover,
            "contain" => TimelineRangeName.Contain,
            "entry" => TimelineRangeName.Entry,
            "exit" => TimelineRangeName.Exit,
            "entry-crossing" => TimelineRangeName.EntryCrossing,
            "exit-crossing" => TimelineRangeName.ExitCrossing,
            "scroll" => TimelineRangeName.Scroll,
            _ => TimelineRangeName.None,
        };

        return name != TimelineRangeName.None;
    }

    /// <summary>
    /// Parses an item of the computed value of <c>animation-range-start</c> or <c>animation-range-end</c>
    /// (https://drafts.csswg.org/scroll-animations-1/#animation-range-start): <c>normal</c> (null), a &lt;length-percentage&gt;,
    /// or a &lt;timeline-range-name&gt; followed by an optional &lt;length-percentage&gt; (0% for the start, 100% for the end).
    /// </summary>
    public static TimelineRangeBoundary? Parse(string item, bool isEnd)
    {
        var values = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(item));
        return TryParse(values, isEnd, allowRelativeUnits: true, out var result) ? result : null;
    }

    private static bool TryParse(List<CssComponentValue> values, bool isEnd, bool allowRelativeUnits, out TimelineRangeBoundary? result)
    {
        result = null;
        var name = TimelineRangeName.None;
        var index = 0;
        if (values.Count > 0 && values[0] is CssPreservedToken { Type: CssTokenType.Ident } ident)
        {
            if (ident.Token.IsIdent("normal") && values.Count == 1)
                return true;

            if (!TryParseName(ident.Token.Value, out name))
                return false;

            index = 1;
        }

        if (index == values.Count)
        {
            if (name == TimelineRangeName.None)
                return false;

            var percentage = isEnd ? 100 : 0;
            result = new TimelineRangeBoundary(name, LengthPercentage.FromPercentage(percentage), new CssUnitValue(percentage, "percent"));
            return true;
        }

        if (index != values.Count - 1)
            return false;

        var component = values[index];
        if (!allowRelativeUnits && HasRelativeUnits(component))
            return false;

        if (!TryParseOffset(component, out var offset))
            return false;

        result = new TimelineRangeBoundary(name, offset, ToNumericValue(component, offset));
        return true;
    }

    /// <summary>Parses a &lt;length-percentage&gt; whose absolute lengths are converted to px.</summary>
    private static bool TryParseOffset(CssComponentValue component, out LengthPercentage offset)
    {
        if (component is CssPreservedToken { Type: CssTokenType.Dimension } dimension && !string.Equals(dimension.Token.Value, "px", StringComparison.OrdinalIgnoreCase))
        {
            if (CssUnits.Convert(dimension.Token.NumericValue, InfraAscii.ToAsciiLowercase(dimension.Token.Value), "px") is { } pixels)
            {
                offset = LengthPercentage.FromPixels(pixels);
                return true;
            }

            offset = default;
            return false;
        }

        return LengthPercentage.TryParse(component, zoom: 1, nonNegative: false, out offset);
    }

    /// <summary>Parses a &lt;length-percentage&gt; whose lengths are absolute (the script API rejects the relative lengths, e.g. em or vh).</summary>
    public static bool TryParseAbsoluteOffset(CssComponentValue component, out LengthPercentage offset)
    {
        if (HasRelativeUnits(component))
        {
            offset = default;
            return false;
        }

        return TryParseOffset(component, out offset);
    }

    /// <summary>Whether a value has a length whose unit is relative (e.g. em or vh): the script API accepts the absolute lengths only.</summary>
    private static bool HasRelativeUnits(CssComponentValue component)
    {
        switch (component)
        {
            case CssPreservedToken { Type: CssTokenType.Dimension } dimension:
                return CssUnits.Convert(1, InfraAscii.ToAsciiLowercase(dimension.Token.Value), "px") is null;
            case CssFunction function:
                foreach (var argument in function.Value)
                {
                    if (HasRelativeUnits(argument))
                        return true;
                }

                return false;
            case CssSimpleBlock block:
                foreach (var value in block.Value)
                {
                    if (HasRelativeUnits(value))
                        return true;
                }

                return false;
            default:
                return false;
        }
    }

    private static CssNumericValue ToNumericValue(CssComponentValue component, LengthPercentage offset)
    {
        if (offset.Calc is null)
        {
            if (!offset.HasPercentage)
                return new CssUnitValue(offset.Length, "px");

            if (offset.Length == 0)
                return new CssUnitValue(offset.Percentage, "percent");
        }

        return CssStyleValueReifier.ReifyNumericValue(component, computed: true, lengthContext: true) ?? new CssUnitValue(offset.Length, "px");
    }

    /// <summary>
    /// Converts the value of <c>rangeStart</c> or <c>rangeEnd</c> of an animation or of the options of <c>animate()</c>
    /// (https://drafts.csswg.org/web-animations-2/#dom-keyframeanimationoptions-rangestart): a string is parsed as an item of
    /// <c>animation-range-start</c>/<c>animation-range-end</c>, a numeric value is an offset in the whole timeline, and only the
    /// keyword <c>normal</c> is accepted. Returns null for <c>normal</c>.
    /// </summary>
    /// <exception cref="TypeErrorException">The value is invalid, or a length is relative (e.g. em, vh).</exception>
    public static TimelineRangeBoundary? FromScriptValue(TimelineRangeValue value, bool isEnd)
    {
        switch (value.Value)
        {
            case null:
                return null;
            case string text:
            {
                var values = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(text));
                if (CssShorthands.ParseValue(isEnd ? "animation-range-end" : "animation-range-start", values, originalText: null) is not [{ ShorthandName: null }] || ValueMatcher.SplitAtCommas(values).Count != 1)
                    throw new TypeErrorException("'" + text + "' is not a valid timeline range.");

                if (!TryParse(values, isEnd, allowRelativeUnits: false, out var result))
                    throw new TypeErrorException("'" + text + "' is not a valid timeline range.");

                return result;
            }

            case CssKeywordValue keyword:
                if (!InfraAscii.EqualsIgnoreAsciiCase(keyword.Value, "normal"))
                    throw new TypeErrorException("The keyword of a timeline range must be 'normal'.");

                return null;
            case CssNumericValue numeric:
                return FromNumericValue(TimelineRangeName.None, numeric);
            case TimelineRangeOffset rangeOffset:
            {
                var name = TimelineRangeName.None;
                if (rangeOffset.RangeName is { } rangeName && !TryParseName(rangeName, out name) && !InfraAscii.EqualsIgnoreAsciiCase(rangeName, "none"))
                    throw new TypeErrorException("'" + rangeName + "' is not a timeline range name.");

                if (rangeOffset.Offset is { } offset)
                    return FromNumericValue(name, offset);

                if (name == TimelineRangeName.None)
                    throw new TypeErrorException("A timeline range offset needs a range name or an offset.");

                var percentage = isEnd ? 100 : 0;
                return new TimelineRangeBoundary(name, LengthPercentage.FromPercentage(percentage), new CssUnitValue(percentage, "percent"));
            }

            default:
                throw new TypeErrorException("The value is not a valid timeline range.");
        }
    }

    private static TimelineRangeBoundary FromNumericValue(TimelineRangeName name, CssNumericValue value)
    {
        var values = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value.ToString()));
        if (values is not [var component] || HasRelativeUnits(component))
            throw new TypeErrorException("The offset of a timeline range must be a length or a percentage with an absolute unit.");

        if (component is CssPreservedToken { Type: CssTokenType.Number } number && number.Token.NumericValue != 0)
            throw new TypeErrorException("The offset of a timeline range must be a length or a percentage.");

        if (!TryParseOffset(component, out var offset))
            throw new TypeErrorException("The offset of a timeline range must be a length or a percentage.");

        // The absolute lengths are converted to px (e.g. CSS.in(1) is reported as CSS.px(96))
        return new TimelineRangeBoundary(name, offset, value is CssUnitValue { Unit: not ("px" or "percent") } ? ToNumericValue(component, offset) : value);
    }

    /// <summary>
    /// Parses a &lt;keyframe-selector&gt; with a timeline range name (https://drafts.csswg.org/scroll-animations-1/#named-range-keyframes):
    /// "&lt;timeline-range-name&gt; &lt;percentage&gt;" (e.g. "entry 10%"), or returns null.
    /// </summary>
    public static TimelineRangeBoundary? ParseKeyframeSelector(List<CssComponentValue> values)
    {
        if (values is not [CssPreservedToken { Type: CssTokenType.Ident } ident, CssPreservedToken { Type: CssTokenType.Percentage } percentage] || !TryParseName(ident.Token.Value, out var name))
            return null;

        return new TimelineRangeBoundary(name, LengthPercentage.FromPercentage(percentage.Token.NumericValue), new CssUnitValue(percentage.Token.NumericValue, "percent"));
    }

    /// <summary>Converts a <see cref="TimelineRangeOffset"/> keyframe offset: a timeline range name and a percentage, or returns null.</summary>
    public static TimelineRangeBoundary? FromKeyframeOffset(string? rangeName, CssNumericValue? offset)
    {
        if (rangeName is null || !TryParseName(rangeName, out var name))
            return null;

        if (offset is null)
            return new TimelineRangeBoundary(name, LengthPercentage.FromPercentage(0), new CssUnitValue(0, "percent"));

        return offset is CssUnitValue { Unit: "percent" } percent ? new TimelineRangeBoundary(name, LengthPercentage.FromPercentage(percent.Value), percent) : null;
    }

    /// <summary>Returns the value of <c>rangeStart</c> or <c>rangeEnd</c> (https://drafts.csswg.org/web-animations-2/#dom-animation-rangestart): "normal" or a <see cref="TimelineRangeOffset"/>.</summary>
    public static TimelineRangeValue ToScriptValue(TimelineRangeBoundary? boundary)
    {
        if (boundary is null)
            return "normal";

        return new TimelineRangeOffset { RangeName = GetName(boundary.Name), Offset = boundary.OffsetValue };
    }

    public bool Equals([NotNullWhen(true)] TimelineRangeBoundary? other) => other is not null && Name == other.Name && Offset == other.Offset && Zoom == other.Zoom;

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is TimelineRangeBoundary other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Name, Offset);
}

/// <summary>A point of a timeline given by a named timeline range and an offset (https://drafts.csswg.org/web-animations-2/#dictdef-timelinerangeoffset).</summary>
[IdlInterface("TimelineRangeOffset", "https://drafts.csswg.org/web-animations-2/#dictdef-timelinerangeoffset")]
public sealed class TimelineRangeOffset
{
    /// <summary>Gets the named timeline range (e.g. <c>cover</c>, <c>entry</c>), or null for the whole timeline.</summary>
    [IdlMember("rangeName", "https://drafts.csswg.org/web-animations-2/#dom-timelinerangeoffset-rangename")]
    public string? RangeName { get; init; }

    /// <summary>Gets the offset in the range: a length or a percentage of the range.</summary>
    [IdlMember("offset", "https://drafts.csswg.org/web-animations-2/#dom-timelinerangeoffset-offset")]
    public CssNumericValue? Offset { get; init; }
}

/// <summary>
/// The (TimelineRangeOffset or CSSNumericValue or CSSKeywordValue or DOMString) union of <c>rangeStart</c> and <c>rangeEnd</c>
/// (https://drafts.csswg.org/web-animations-2/#dom-keyframeanimationoptions-rangestart).
/// </summary>
public readonly struct TimelineRangeValue : IUnionValue, IEquatable<TimelineRangeValue>
{
    private TimelineRangeValue(object value) => Value = value;

    /// <summary>Gets the value: a <see cref="TimelineRangeOffset"/>, a <see cref="CssNumericValue"/>, a <see cref="CssKeywordValue"/> or a <see cref="string"/>.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Converts a <c>TimelineRangeOffset</c> to a <c>TimelineRangeValue</c>.</summary>
    [NotInIdl]
    public static implicit operator TimelineRangeValue(TimelineRangeOffset value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts a <c>CssNumericValue</c> to a <c>TimelineRangeValue</c>.</summary>
    [NotInIdl]
    public static implicit operator TimelineRangeValue(CssNumericValue value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts a <c>CssKeywordValue</c> to a <c>TimelineRangeValue</c>.</summary>
    [NotInIdl]
    public static implicit operator TimelineRangeValue(CssKeywordValue value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts a string (e.g. "normal", "entry 10%") to a <c>TimelineRangeValue</c>.</summary>
    [NotInIdl]
    public static implicit operator TimelineRangeValue(string value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates a <c>TimelineRangeValue</c> from a <c>TimelineRangeOffset</c>.</summary>
    [NotInIdl]
    public static TimelineRangeValue FromTimelineRangeOffset(TimelineRangeOffset value) => value;

    /// <summary>Creates a <c>TimelineRangeValue</c> from a <c>CssNumericValue</c>.</summary>
    [NotInIdl]
    public static TimelineRangeValue FromCssNumericValue(CssNumericValue value) => value;

    /// <summary>Creates a <c>TimelineRangeValue</c> from a <c>CssKeywordValue</c>.</summary>
    [NotInIdl]
    public static TimelineRangeValue FromCssKeywordValue(CssKeywordValue value) => value;

    /// <summary>Creates a <c>TimelineRangeValue</c> from a string.</summary>
    [NotInIdl]
    public static TimelineRangeValue FromString(string value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(TimelineRangeValue other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is TimelineRangeValue other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <summary>Determines whether two <c>TimelineRangeValue</c> values are equal.</summary>
    [NotInIdl]
    public static bool operator ==(TimelineRangeValue left, TimelineRangeValue right) => left.Equals(right);

    /// <summary>Determines whether two <c>TimelineRangeValue</c> values are not equal.</summary>
    [NotInIdl]
    public static bool operator !=(TimelineRangeValue left, TimelineRangeValue right) => !left.Equals(right);
}
