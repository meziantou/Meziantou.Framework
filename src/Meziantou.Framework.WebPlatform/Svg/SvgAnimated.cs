using Meziantou.Framework.WebPlatform.Svg;

namespace Meziantou.Framework.WebPlatform.Dom;

// The base values reflect the content attributes; the animated values (animVal) are the values of the SMIL animations in
// effect (https://svgwg.org/specs/animations/#TermAnimatedValue), or the base values when the attribute is not animated
// (https://svgwg.org/svg2-draft/types.html#InterfaceSVGAnimatedBoolean).

/// <summary>https://svgwg.org/svg2-draft/types.html#InterfaceSVGAnimatedBoolean</summary>
[IdlInterface("SVGAnimatedBoolean", "https://w3c.github.io/svgwg/svg2-draft/types.html#InterfaceSVGAnimatedBoolean")]
public sealed class SvgAnimatedBoolean
{
    private readonly Element _element;
    private readonly string _name;

    internal SvgAnimatedBoolean(Element element, string name)
    {
        _element = element;
        _name = name;
    }

    /// <summary>Gets or sets the base value, which reflects the content attribute.</summary>
    [IdlMember("baseVal", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGAnimatedBoolean__baseVal")]
    public bool BaseVal
    {
        get => _element.GetAttributeValue(_name) is { } value && value.Trim(" \t\n\f\r".ToCharArray()) == "true";
        set => _element.SetAttributeValue(_name, value ? "true" : "false");
    }

    /// <summary>Gets the animated value: the value of the animations in effect, or the base value.</summary>
    [IdlMember("animVal", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGAnimatedBoolean__animVal")]
    public bool AnimVal => _element.GetSampledAnimatedAttributeValue(_name) is { } value && value.Trim(" \t\n\f\r".ToCharArray()) == "true";
}

/// <summary>https://svgwg.org/svg2-draft/types.html#InterfaceSVGAnimatedString</summary>
[IdlInterface("SVGAnimatedString", "https://w3c.github.io/svgwg/svg2-draft/types.html#InterfaceSVGAnimatedString")]
public sealed class SvgAnimatedString
{
    private const string XLinkNamespace = "http://www.w3.org/1999/xlink";

    private readonly Element _element;
    private readonly string _name;
    private readonly bool _xlinkFallback;

    internal SvgAnimatedString(Element element, string name, bool xlinkFallback = false)
    {
        _element = element;
        _name = name;
        _xlinkFallback = xlinkFallback;
    }

    /// <summary>
    /// https://svgwg.org/svg2-draft/types.html#__svg__SVGAnimatedString__baseVal: the href attribute falls back to the
    /// deprecated xlink:href attribute (https://svgwg.org/svg2-draft/linking.html#XLinkRefAttrs).
    /// </summary>
    [IdlMember("baseVal", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGAnimatedString__baseVal")]
    public string BaseVal
    {
        get
        {
            var value = _element.GetAttributeValue(_name);
            if (value is null && _xlinkFallback)
            {
                value = _element.GetAttributeNS(XLinkNamespace, "href");
            }

            return value ?? "";
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            SetBaseValCore(value);
        }
    }

    /// <summary>
    /// The baseVal setter with a <see cref="TrustedScriptUrl"/> value
    /// (https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGAnimatedString__baseVal): the value of the href attribute
    /// of a script element must be a <see cref="TrustedScriptUrl"/> when Trusted Types are enforced.
    /// </summary>
    [IdlMember("baseVal", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGAnimatedString__baseVal")]
    public void SetBaseVal(TrustedScriptUrl value)
    {
        ArgumentNullException.ThrowIfNull(value);
        SetBaseValCore(value);
    }

    /// <param name="value">A <see cref="string"/> or a <see cref="TrustedScriptUrl"/>.</param>
    private void SetBaseValCore(object value)
    {
        if (_xlinkFallback && _element.GetAttributeValue(_name) is null && _element.HasAttributeNS(XLinkNamespace, "href"))
        {
            _element.SetAttributeValue("href", TrustedTypesAlgorithms.GetCompliantAttributeValue("href", XLinkNamespace, _element, value), "xlink", XLinkNamespace);
        }
        else
        {
            _element.SetAttributeValue(_name, TrustedTypesAlgorithms.GetCompliantAttributeValue(_name, attributeNs: null, _element, value));
        }
    }

    /// <summary>Gets the animated value: the value of the animations in effect, or the base value.</summary>
    [IdlMember("animVal", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGAnimatedString__animVal")]
    public string AnimVal
    {
        get
        {
            if (_element.NodeDocument.SmilOrNull is { } smil)
            {
                smil.Flush();
                if (smil.GetAttributeValue(_element, _name) is { } animated)
                    return animated;
            }

            return BaseVal;
        }
    }
}

/// <summary>https://svgwg.org/svg2-draft/types.html#InterfaceSVGAnimatedEnumeration</summary>
[IdlInterface("SVGAnimatedEnumeration", "https://w3c.github.io/svgwg/svg2-draft/types.html#InterfaceSVGAnimatedEnumeration")]
public sealed class SvgAnimatedEnumeration
{
    private readonly Element _element;
    private readonly string _name;
    private readonly SvgEnumeration _enumeration;

    internal SvgAnimatedEnumeration(Element element, string name, SvgEnumeration enumeration)
    {
        _element = element;
        _name = name;
        _enumeration = enumeration;
    }

    /// <summary>Gets or sets the base value, which reflects the content attribute.</summary>
    [IdlMember("baseVal", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGAnimatedEnumeration__baseVal")]
    public ushort BaseVal
    {
        get => _enumeration.GetValue(_element.GetAttributeValue(_name));
        set
        {
            var keyword = _enumeration.GetKeyword(value) ?? throw new TypeErrorException($"{value} is not a valid value");
            _element.SetAttributeValue(_name, keyword);
        }
    }

    /// <summary>Gets the animated value: the value of the animations in effect, or the base value.</summary>
    [IdlMember("animVal", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGAnimatedEnumeration__animVal")]
    public ushort AnimVal => _enumeration.GetValue(_element.GetSampledAnimatedAttributeValue(_name));
}

/// <summary>The keywords of an enumerated SVG attribute and their numeric values.</summary>
internal class SvgEnumeration(ushort defaultValue, params (string Keyword, ushort Value)[] keywords)
{
    /// <summary>The orientType of markers (https://svgwg.org/svg2-draft/painting.html#__svg__SVGMarkerElement__orientType): an angle is SVG_MARKER_ORIENT_ANGLE.</summary>
    public static SvgEnumeration MarkerOrient { get; } = new(2, ("auto", 1), ("0", 2), ("auto-start-reverse", 3));

    public virtual ushort GetValue(string? attributeValue)
    {
        if (attributeValue is not null)
        {
            var trimmed = attributeValue.Trim(" \t\n\f\r".ToCharArray());
            foreach (var (keyword, value) in keywords)
            {
                if (keyword == trimmed)
                    return value;
            }
        }

        return defaultValue;
    }

    /// <summary>Whether a value is a keyword of the enumeration.</summary>
    public virtual bool IsKeyword(string value)
    {
        foreach (var (keyword, _) in keywords)
        {
            if (keyword == value)
                return true;
        }

        return false;
    }

    public string? GetKeyword(ushort value)
    {
        foreach (var (keyword, keywordValue) in keywords)
        {
            if (keywordValue == value && value != 0)
                return keyword;
        }

        return null;
    }
}

/// <summary>https://svgwg.org/svg2-draft/types.html#InterfaceSVGAnimatedInteger</summary>
[IdlInterface("SVGAnimatedInteger", "https://w3c.github.io/svgwg/svg2-draft/types.html#InterfaceSVGAnimatedInteger")]
public sealed class SvgAnimatedInteger
{
    private readonly SvgNumberPair _pair;

    internal SvgAnimatedInteger(Element element, string name, int component, float defaultValue)
    {
        _pair = new SvgNumberPair(element, name, component, defaultValue, integer: true);
    }

    /// <summary>Gets or sets the base value, which reflects the content attribute.</summary>
    [IdlMember("baseVal", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGAnimatedInteger__baseVal")]
    public int BaseVal
    {
        get => (int)_pair.Get();
        set => _pair.Set(value);
    }

    /// <summary>Gets the animated value: the value of the animations in effect, or the base value.</summary>
    [IdlMember("animVal", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGAnimatedInteger__animVal")]
    public int AnimVal => (int)_pair.Get(animated: true);
}

/// <summary>https://svgwg.org/svg2-draft/types.html#InterfaceSVGAnimatedNumber</summary>
[IdlInterface("SVGAnimatedNumber", "https://w3c.github.io/svgwg/svg2-draft/types.html#InterfaceSVGAnimatedNumber")]
public sealed class SvgAnimatedNumber
{
    private readonly SvgNumberPair _pair;

    internal SvgAnimatedNumber(Element element, string name, int component, float defaultValue)
    {
        _pair = new SvgNumberPair(element, name, component, defaultValue, integer: false);
    }

    /// <summary>Gets or sets the base value, which reflects the content attribute.</summary>
    [IdlMember("baseVal", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGAnimatedNumber__baseVal")]
    public float BaseVal
    {
        get => _pair.Get();
        set => _pair.Set(value);
    }

    /// <summary>Gets the animated value: the value of the animations in effect, or the base value.</summary>
    [IdlMember("animVal", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGAnimatedNumber__animVal")]
    public float AnimVal => _pair.Get(animated: true);
}

/// <summary>
/// A number attribute, or a component of a number-optional-number attribute (e.g. stdDeviation="2 3"): component -1 is
/// a single number, 0 the first number, 1 the second number (which defaults to the first one).
/// </summary>
internal sealed class SvgNumberPair(Element element, string name, int component, float defaultValue, bool integer)
{
    private bool TryGetNumbers(out float first, out float second, bool animated = false)
    {
        first = second = defaultValue;
        var value = animated ? element.GetSampledAnimatedAttributeValue(name) : element.GetAttributeValue(name);
        if (component < 0)
        {
            if (integer ? !TryParseInteger(value, out first) : !SvgParsing.TryParseSingleNumber(value, out first))
            {
                first = defaultValue;
                return false;
            }

            return true;
        }

        if (value is null || !SvgParsing.TryParseNumberList(value, out var numbers) || numbers.Count is 0 or > 2 || (integer && numbers.Exists(n => n != MathF.Truncate(n))))
            return false;

        first = numbers[0];
        second = numbers.Count == 2 ? numbers[1] : numbers[0];
        return true;
    }

    private static bool TryParseInteger(string? value, out float result)
    {
        result = 0;
        if (!SvgParsing.TryParseSingleNumber(value, out var number) || number != MathF.Truncate(number))
            return false;

        result = number;
        return true;
    }

    /// <param name="animated">Whether the animated value is returned (animVal), rather than the base value.</param>
    public float Get(bool animated = false)
    {
        TryGetNumbers(out var first, out var second, animated);
        return component == 1 ? second : first;
    }

    public void Set(float value)
    {
        if (component < 0)
        {
            element.SetAttributeValue(name, SvgParsing.FormatNumber(value));
            return;
        }

        TryGetNumbers(out var first, out var second);
        if (component == 0)
        {
            first = value;
        }
        else
        {
            second = value;
        }

        element.SetAttributeValue(name, SvgParsing.FormatNumber(first) + " " + SvgParsing.FormatNumber(second));
    }
}

/// <summary>A single SVG value object (length, angle...) that reflects an attribute, with a default value when the attribute is missing or invalid.</summary>
internal sealed class SvgReflectedValue(SvgAttributeBinding binding, Func<string?, bool> parse, Func<string> serialize) : ISvgValueOwner
{
    public bool IsReadOnly => binding.IsReadOnly;

    public void Sync()
    {
        if (binding.HasChanged(out var value))
        {
            parse(value);
        }
    }

    public void Commit() => binding.Write(serialize());
}

/// <summary>https://svgwg.org/svg2-draft/types.html#InterfaceSVGAnimatedLength</summary>
[IdlInterface("SVGAnimatedLength", "https://w3c.github.io/svgwg/svg2-draft/types.html#InterfaceSVGAnimatedLength")]
public sealed class SvgAnimatedLength
{
    internal SvgAnimatedLength(Element element, string name, string defaultValue)
    {
        BaseVal = CreateLength(element, name, defaultValue, readOnly: false);
        AnimVal = CreateLength(element, name, defaultValue, readOnly: true);
    }

    private static SvgLength CreateLength(Element element, string name, string defaultValue, bool readOnly)
    {
        var length = new SvgLength { Context = (element, name) };
        length.Owner = new SvgReflectedValue(
            new SvgAttributeBinding(element, name, readOnly),
            value =>
            {
                // https://svgwg.org/svg2-draft/geometry.html#Sizing, https://svgwg.org/svg2-draft/geometry.html#R: a negative value
                // of the sizes and of the radii is invalid: it is ignored
                if (value is null || !SvgLength.TryParse(value, out var number, out var unitType, out var unknownUnit) || (number < 0 && name is "r" or "rx" or "ry" or "width" or "height"))
                {
                    _ = SvgLength.TryParse(defaultValue, out number, out unitType, out unknownUnit);
                }

                length.SetRaw(number, unitType, unknownUnit);
                return true;
            },
            length.Serialize);
        return length;
    }

    /// <summary>Gets the base value, which reflects the content attribute.</summary>
    [IdlMember("baseVal", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGAnimatedLength__baseVal")]
    public SvgLength BaseVal { get; }

    /// <summary>
    /// Gets the read-only animated value: the value of the animations in effect, or the base value.
    /// </summary>
    [IdlMember("animVal", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGAnimatedLength__animVal")]
    public SvgLength AnimVal { get; }
}

/// <summary>https://svgwg.org/svg2-draft/types.html#InterfaceSVGAnimatedAngle</summary>
[IdlInterface("SVGAnimatedAngle", "https://w3c.github.io/svgwg/svg2-draft/types.html#InterfaceSVGAnimatedAngle")]
public sealed class SvgAnimatedAngle
{
    internal SvgAnimatedAngle(Element element, string name)
    {
        BaseVal = CreateAngle(element, name, readOnly: false);
        AnimVal = CreateAngle(element, name, readOnly: true);
    }

    private static SvgAngle CreateAngle(Element element, string name, bool readOnly)
    {
        var angle = new SvgAngle();
        angle.Owner = new SvgReflectedValue(
            new SvgAttributeBinding(element, name, readOnly),
            value =>
            {
                // "auto" and "auto-start-reverse" (orient) and invalid values are the angle 0
                if (value is null || !angle.TrySetRaw(value))
                {
                    angle.SetRaw(0, SvgAngle.SvgAngleTypeUnspecified);
                }

                return true;
            },
            angle.Serialize);
        return angle;
    }

    /// <summary>Gets the base value, which reflects the content attribute.</summary>
    [IdlMember("baseVal", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGAnimatedAngle__baseVal")]
    public SvgAngle BaseVal { get; }

    /// <summary>
    /// Gets the read-only animated value: the value of the animations in effect, or the base value.
    /// </summary>
    [IdlMember("animVal", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGAnimatedAngle__animVal")]
    public SvgAngle AnimVal { get; }
}

/// <summary>https://svgwg.org/svg2-draft/coords.html#InterfaceSVGAnimatedPreserveAspectRatio</summary>
[IdlInterface("SVGAnimatedPreserveAspectRatio", "https://w3c.github.io/svgwg/svg2-draft/coords.html#InterfaceSVGAnimatedPreserveAspectRatio")]
public sealed class SvgAnimatedPreserveAspectRatio
{
    internal SvgAnimatedPreserveAspectRatio(Element element, string name)
    {
        BaseVal = Create(element, name, readOnly: false);
        AnimVal = Create(element, name, readOnly: true);
    }

    private static SvgPreserveAspectRatio Create(Element element, string name, bool readOnly)
    {
        SvgPreserveAspectRatio? result = null;
        var owner = new SvgReflectedValue(
            new SvgAttributeBinding(element, name, readOnly),
            value =>
            {
                _ = SvgPreserveAspectRatio.TryParse(value, out var align, out var meetOrSlice);
                result!.SetRaw(align, meetOrSlice);
                return true;
            },
            () => result!.Serialize());
        result = new SvgPreserveAspectRatio(owner);
        return result;
    }

    /// <summary>Gets the base value, which reflects the content attribute.</summary>
    [IdlMember("baseVal", "https://w3c.github.io/svgwg/svg2-draft/coords.html#__svg__SVGAnimatedPreserveAspectRatio__baseVal")]
    public SvgPreserveAspectRatio BaseVal { get; }

    /// <summary>
    /// Gets the read-only animated value: the value of the animations in effect, or the base value.
    /// </summary>
    [IdlMember("animVal", "https://w3c.github.io/svgwg/svg2-draft/coords.html#__svg__SVGAnimatedPreserveAspectRatio__animVal")]
    public SvgPreserveAspectRatio AnimVal { get; }
}

/// <summary>https://svgwg.org/svg2-draft/types.html#InterfaceSVGAnimatedRect</summary>
[IdlInterface("SVGAnimatedRect", "https://w3c.github.io/svgwg/svg2-draft/types.html#InterfaceSVGAnimatedRect")]
public sealed class SvgAnimatedRect
{
    private readonly SvgReflectedRect _baseVal;
    private readonly SvgReflectedRect _animVal;

    internal SvgAnimatedRect(Element element, string name)
    {
        _baseVal = new SvgReflectedRect(new SvgAttributeBinding(element, name, readOnly: false));
        _animVal = new SvgReflectedRect(new SvgAttributeBinding(element, name, readOnly: true));
    }

    /// <summary>The rectangle (all zeros when the attribute is missing or invalid).</summary>
    [IdlMember("baseVal", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGAnimatedRect__baseVal")]
    public DomRect BaseVal => _baseVal.GetValue();

    /// <summary>Gets the animated value: the value of the animations in effect, or the base value.</summary>
    [IdlMember("animVal", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGAnimatedRect__animVal")]
    public DomRect AnimVal => _animVal.GetValue();

    /// <summary>A viewBox rectangle: changes are written to the attribute.</summary>
    private sealed class SvgReflectedRect(SvgAttributeBinding binding) : DomRect
    {
        public SvgReflectedRect GetValue()
        {
            Sync();
            return this;
        }

        private void Sync()
        {
            if (!binding.HasChanged(out var value))
                return;

            var valid = SvgParsing.TryParseNumberList(value, out var numbers) && numbers.Count == 4 && numbers[2] >= 0 && numbers[3] >= 0;
            XValue = valid ? numbers[0] : 0;
            YValue = valid ? numbers[1] : 0;
            WidthValue = valid ? numbers[2] : 0;
            HeightValue = valid ? numbers[3] : 0;
        }

        private void Set(Action action)
        {
            if (binding.IsReadOnly)
                throw DomException.NoModificationAllowed("The rectangle is read only");

            Sync();
            action();
            binding.Write(SvgParsing.FormatNumber((float)XValue) + " " + SvgParsing.FormatNumber((float)YValue) + " " + SvgParsing.FormatNumber((float)WidthValue) + " " + SvgParsing.FormatNumber((float)HeightValue));
        }

        public override double X { get => base.X; set => Set(() => XValue = value); }

        public override double Y { get => base.Y; set => Set(() => YValue = value); }

        public override double Width { get => base.Width; set => Set(() => WidthValue = value); }

        public override double Height { get => base.Height; set => Set(() => HeightValue = value); }
    }
}

/// <summary>https://svgwg.org/svg2-draft/types.html#InterfaceSVGAnimatedNumberList</summary>
[IdlInterface("SVGAnimatedNumberList", "https://w3c.github.io/svgwg/svg2-draft/types.html#InterfaceSVGAnimatedNumberList")]
public sealed class SvgAnimatedNumberList
{
    internal SvgAnimatedNumberList(Element element, string name)
    {
        BaseVal = new SvgNumberList(new SvgAttributeBinding(element, name, readOnly: false));
        AnimVal = new SvgNumberList(new SvgAttributeBinding(element, name, readOnly: true));
    }

    /// <summary>Gets the base value, which reflects the content attribute.</summary>
    [IdlMember("baseVal", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGAnimatedNumberList__baseVal")]
    public SvgNumberList BaseVal { get; }

    /// <summary>
    /// Gets the read-only animated value: the value of the animations in effect, or the base value.
    /// </summary>
    [IdlMember("animVal", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGAnimatedNumberList__animVal")]
    public SvgNumberList AnimVal { get; }
}

/// <summary>https://svgwg.org/svg2-draft/types.html#InterfaceSVGAnimatedLengthList</summary>
[IdlInterface("SVGAnimatedLengthList", "https://w3c.github.io/svgwg/svg2-draft/types.html#InterfaceSVGAnimatedLengthList")]
public sealed class SvgAnimatedLengthList
{
    internal SvgAnimatedLengthList(Element element, string name)
    {
        BaseVal = new SvgLengthList(new SvgAttributeBinding(element, name, readOnly: false));
        AnimVal = new SvgLengthList(new SvgAttributeBinding(element, name, readOnly: true));
    }

    /// <summary>Gets the base value, which reflects the content attribute.</summary>
    [IdlMember("baseVal", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGAnimatedLengthList__baseVal")]
    public SvgLengthList BaseVal { get; }

    /// <summary>
    /// Gets the read-only animated value: the value of the animations in effect, or the base value.
    /// </summary>
    [IdlMember("animVal", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGAnimatedLengthList__animVal")]
    public SvgLengthList AnimVal { get; }
}

/// <summary>https://svgwg.org/svg2-draft/coords.html#InterfaceSVGAnimatedTransformList</summary>
[IdlInterface("SVGAnimatedTransformList", "https://w3c.github.io/svgwg/svg2-draft/coords.html#InterfaceSVGAnimatedTransformList")]
public sealed class SvgAnimatedTransformList
{
    internal SvgAnimatedTransformList(Element element, string name)
    {
        BaseVal = new SvgTransformList(new SvgAttributeBinding(element, name, readOnly: false));
        AnimVal = new SvgTransformList(new SvgAttributeBinding(element, name, readOnly: true));
    }

    /// <summary>Gets the base value, which reflects the content attribute.</summary>
    [IdlMember("baseVal", "https://w3c.github.io/svgwg/svg2-draft/coords.html#__svg__SVGAnimatedTransformList__baseVal")]
    public SvgTransformList BaseVal { get; }

    /// <summary>
    /// Gets the read-only animated value: the value of the animations in effect, or the base value.
    /// </summary>
    [IdlMember("animVal", "https://w3c.github.io/svgwg/svg2-draft/coords.html#__svg__SVGAnimatedTransformList__animVal")]
    public SvgTransformList AnimVal { get; }
}
