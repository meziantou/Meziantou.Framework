using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Svg;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The owner of an SVG value object that reflects an attribute (an animated value or a list): the value is read from the
/// attribute before each access, and written to the attribute after each change.
/// </summary>
internal interface ISvgValueOwner
{
    /// <summary>Whether the value is read only (animVal): changes throw a NoModificationAllowedError.</summary>
    bool IsReadOnly { get; }

    /// <summary>Updates the value from the attribute when it changed.</summary>
    void Sync();

    /// <summary>Writes the value to the attribute.</summary>
    void Commit();
}

internal static class SvgValueOwnerExtensions
{
    public static void BeginChange(this ISvgValueOwner? owner)
    {
        if (owner is null)
            return;

        if (owner.IsReadOnly)
            throw DomException.NoModificationAllowed("The object is read only");

        owner.Sync();
    }
}

/// <summary>https://svgwg.org/svg2-draft/types.html#InterfaceSVGUnitTypes</summary>
[IdlInterface("SVGUnitTypes", "https://w3c.github.io/svgwg/svg2-draft/types.html#InterfaceSVGUnitTypes")]
public static class SvgUnitTypes
{
    /// <summary>The unit type is not one of the predefined types.</summary>
    [IdlMember("SVG_UNIT_TYPE_UNKNOWN", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGUnitTypes__SVG_UNIT_TYPE_UNKNOWN")]
    public const ushort SvgUnitTypeUnknown = 0;

    /// <summary>The coordinates are in the user coordinate system in place at the time of use.</summary>
    [IdlMember("SVG_UNIT_TYPE_USERSPACEONUSE", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGUnitTypes__SVG_UNIT_TYPE_USERSPACEONUSE")]
    public const ushort SvgUnitTypeUserSpaceOnUse = 1;

    /// <summary>The coordinates are fractions of the bounding box of the element.</summary>
    [IdlMember("SVG_UNIT_TYPE_OBJECTBOUNDINGBOX", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGUnitTypes__SVG_UNIT_TYPE_OBJECTBOUNDINGBOX")]
    public const ushort SvgUnitTypeObjectBoundingBox = 2;
}

/// <summary>https://svgwg.org/svg2-draft/types.html#InterfaceSVGNumber</summary>
[IdlInterface("SVGNumber", "https://w3c.github.io/svgwg/svg2-draft/types.html#InterfaceSVGNumber")]
public sealed class SvgNumber
{
    private float _value;

    internal SvgNumber(float value = 0, ISvgValueOwner? owner = null)
    {
        _value = value;
        Owner = owner;
    }

    internal ISvgValueOwner? Owner { get; set; }

    /// <summary>Gets or sets the number.</summary>
    [IdlMember("value", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGNumber__value")]
    public float Value
    {
        get
        {
            Owner?.Sync();
            return _value;
        }
        set
        {
            Owner.BeginChange();
            _value = value;
            Owner?.Commit();
        }
    }

    internal float RawValue
    {
        get => _value;
        set => _value = value;
    }

    internal SvgNumber Copy() => new(_value);

    internal string Serialize() => SvgParsing.FormatNumber(_value);
}

/// <summary>https://svgwg.org/svg2-draft/types.html#InterfaceSVGLength</summary>
[IdlInterface("SVGLength", "https://w3c.github.io/svgwg/svg2-draft/types.html#InterfaceSVGLength")]
public sealed class SvgLength
{
    /// <summary>The unit is not one of the predefined units.</summary>
    [IdlMember("SVG_LENGTHTYPE_UNKNOWN", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGLength__SVG_LENGTHTYPE_UNKNOWN")]
    public const ushort SvgLengthTypeUnknown = 0;

    /// <summary>A number without unit, in user units.</summary>
    [IdlMember("SVG_LENGTHTYPE_NUMBER", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGLength__SVG_LENGTHTYPE_NUMBER")]
    public const ushort SvgLengthTypeNumber = 1;

    /// <summary>A percentage.</summary>
    [IdlMember("SVG_LENGTHTYPE_PERCENTAGE", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGLength__SVG_LENGTHTYPE_PERCENTAGE")]
    public const ushort SvgLengthTypePercentage = 2;

    /// <summary>A length in <c>em</c>.</summary>
    [IdlMember("SVG_LENGTHTYPE_EMS", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGLength__SVG_LENGTHTYPE_EMS")]
    public const ushort SvgLengthTypeEms = 3;

    /// <summary>A length in <c>ex</c>.</summary>
    [IdlMember("SVG_LENGTHTYPE_EXS", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGLength__SVG_LENGTHTYPE_EXS")]
    public const ushort SvgLengthTypeExs = 4;

    /// <summary>A length in <c>px</c>.</summary>
    [IdlMember("SVG_LENGTHTYPE_PX", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGLength__SVG_LENGTHTYPE_PX")]
    public const ushort SvgLengthTypePx = 5;

    /// <summary>A length in <c>cm</c>.</summary>
    [IdlMember("SVG_LENGTHTYPE_CM", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGLength__SVG_LENGTHTYPE_CM")]
    public const ushort SvgLengthTypeCm = 6;

    /// <summary>A length in <c>mm</c>.</summary>
    [IdlMember("SVG_LENGTHTYPE_MM", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGLength__SVG_LENGTHTYPE_MM")]
    public const ushort SvgLengthTypeMm = 7;

    /// <summary>A length in <c>in</c>.</summary>
    [IdlMember("SVG_LENGTHTYPE_IN", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGLength__SVG_LENGTHTYPE_IN")]
    public const ushort SvgLengthTypeIn = 8;

    /// <summary>A length in <c>pt</c>.</summary>
    [IdlMember("SVG_LENGTHTYPE_PT", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGLength__SVG_LENGTHTYPE_PT")]
    public const ushort SvgLengthTypePt = 9;

    /// <summary>A length in <c>pc</c>.</summary>
    [IdlMember("SVG_LENGTHTYPE_PC", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGLength__SVG_LENGTHTYPE_PC")]
    public const ushort SvgLengthTypePc = 10;

    private static readonly string[] Units = ["", "", "%", "em", "ex", "px", "cm", "mm", "in", "pt", "pc"];

    private float _value;
    private ushort _unitType = SvgLengthTypeNumber;

    // The unit of lengths whose unit is not one of the SVG_LENGTHTYPE constants (e.g. "rem")
    private string _unknownUnit = "";

    internal SvgLength(ISvgValueOwner? owner = null)
    {
        Owner = owner;
    }

    internal ISvgValueOwner? Owner { get; set; }

    /// <summary>The element and the attribute the length reflects: the context of the conversions of the relative units, or null.</summary>
    internal (Element Element, string Attribute)? Context { get; set; }

    /// <summary>Gets the type of the unit of the length, one of the <c>SVG_LENGTHTYPE_*</c> constants.</summary>
    [IdlMember("unitType", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGLength__unitType")]
    public ushort UnitType
    {
        get
        {
            Owner?.Sync();
            return _unitType;
        }
    }

    /// <summary>https://svgwg.org/svg2-draft/types.html#__svg__SVGLength__value</summary>
    [IdlMember("value", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGLength__value")]
    public float Value
    {
        get
        {
            Owner?.Sync();
            return (float)(_value * GetUserUnitsFactor(_unitType));
        }
        set
        {
            Owner.BeginChange();
            _value = (float)(value / GetUserUnitsFactor(_unitType));
            Owner?.Commit();
        }
    }

    /// <summary>Gets or sets the value of the length in its own unit.</summary>
    [IdlMember("valueInSpecifiedUnits", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGLength__valueInSpecifiedUnits")]
    public float ValueInSpecifiedUnits
    {
        get
        {
            Owner?.Sync();
            return _value;
        }
        set
        {
            Owner.BeginChange();
            _value = value;
            Owner?.Commit();
        }
    }

    /// <summary>
    /// Gets or sets the serialized length, e.g. <c>10px</c>; setting an invalid length throws a <c>SyntaxError</c>.
    /// </summary>
    [IdlMember("valueAsString", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGLength__valueAsString")]
    public string ValueAsString
    {
        get
        {
            Owner?.Sync();
            return Serialize();
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Owner.BeginChange();
            if (!TryParse(value, out var number, out var unitType, out var unknownUnit))
                throw DomException.Syntax($"'{value}' is not a valid length");

            _value = number;
            _unitType = unitType;
            _unknownUnit = unknownUnit;
            Owner?.Commit();
        }
    }

    /// <summary>https://svgwg.org/svg2-draft/types.html#__svg__SVGLength__newValueSpecifiedUnits</summary>
    [IdlMember("newValueSpecifiedUnits", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGLength__newValueSpecifiedUnits")]
    public void NewValueSpecifiedUnits(ushort unitType, float valueInSpecifiedUnits)
    {
        Owner.BeginChange();
        if (unitType is SvgLengthTypeUnknown or > SvgLengthTypePc)
            throw DomException.NotSupported("The unit type is not supported");

        _value = valueInSpecifiedUnits;
        _unitType = unitType;
        _unknownUnit = "";
        Owner?.Commit();
    }

    /// <summary>https://svgwg.org/svg2-draft/types.html#__svg__SVGLength__convertToSpecifiedUnits</summary>
    [IdlMember("convertToSpecifiedUnits", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGLength__convertToSpecifiedUnits")]
    public void ConvertToSpecifiedUnits(ushort unitType)
    {
        Owner.BeginChange();
        if (unitType is SvgLengthTypeUnknown or > SvgLengthTypePc)
            throw DomException.NotSupported("The unit type is not supported");

        var userUnits = _value * GetUserUnitsFactor(_unitType);
        _value = (float)(userUnits / GetUserUnitsFactor(unitType));
        _unitType = unitType;
        _unknownUnit = "";
        Owner?.Commit();
    }

    /// <summary>
    /// The number of user units of one unit (https://svgwg.org/svg2-draft/types.html#InterfaceSVGLength): the relative units are
    /// resolved for the element the length reflects (its font, its SVG viewport for the percentages, see
    /// <see cref="SvgUserSpace.GetUnitSize"/>). A length without element uses the initial values: a font size of 16px (1em),
    /// 1ex = 0.5em; its percentages and its other relative units cannot be resolved.
    /// </summary>
    private double GetUserUnitsFactor(ushort unitType)
    {
        if (Context is { } context && unitType is not (SvgLengthTypeNumber or SvgLengthTypePx) && SvgUserSpace.GetUnitSize(context.Element, context.Attribute, unitType == SvgLengthTypeUnknown ? _unknownUnit : Units[unitType]) is { } size)
            return size;

        return GetDefaultUserUnitsFactor(unitType);
    }

    private double GetDefaultUserUnitsFactor(ushort unitType) => unitType switch
    {
        SvgLengthTypeNumber or SvgLengthTypePx => 1,
        SvgLengthTypeCm => 96 / 2.54,
        SvgLengthTypeMm => 96 / 25.4,
        SvgLengthTypeIn => 96,
        SvgLengthTypePt => 96.0 / 72,
        SvgLengthTypePc => 16,
        SvgLengthTypeEms => 16,
        SvgLengthTypeExs => 8,
        SvgLengthTypeUnknown when _unknownUnit == "q" => 96 / 2.54 / 40,
        _ => throw DomException.NotSupported($"The length '{Serialize()}' cannot be converted to user units without a viewport"),
    };

    internal void SetRaw(float value, ushort unitType, string unknownUnit)
    {
        _value = value;
        _unitType = unitType;
        _unknownUnit = unknownUnit;
    }

    internal void AssignFrom(SvgLength source) => SetRaw(source._value, source._unitType, source._unknownUnit);

    internal SvgLength Copy()
    {
        var copy = new SvgLength();
        copy.SetRaw(_value, _unitType, _unknownUnit);
        return copy;
    }

    internal string Serialize() => SvgParsing.FormatNumber(_value) + (_unitType == SvgLengthTypeUnknown ? _unknownUnit : Units[_unitType]);

    /// <summary>https://svgwg.org/svg2-draft/types.html#DataTypeLength</summary>
    internal static bool TryParse(string value, out float number, out ushort unitType, out string unknownUnit)
    {
        number = 0;
        unitType = SvgLengthTypeNumber;
        unknownUnit = "";
        var position = 0;
        SvgParsing.SkipWhitespace(value, ref position);
        if (!SvgParsing.TryParseNumber(value, ref position, out var parsed) || !float.IsFinite((float)parsed))
            return false;

        var end = value.Length;
        while (end > position && InfraAscii.IsAsciiWhitespace(value[end - 1]))
        {
            end--;
        }

        var unit = value[position..end];
        number = (float)parsed;
        if (unit.Length == 0)
            return true;

        if (unit == "%")
        {
            unitType = SvgLengthTypePercentage;
            return true;
        }

        for (ushort i = SvgLengthTypeEms; i <= SvgLengthTypePc; i++)
        {
            if (InfraAscii.EqualsIgnoreAsciiCase(unit, Units[i]))
            {
                unitType = i;
                return true;
            }
        }

        // Other CSS length units (https://drafts.csswg.org/css-values/#lengths)
        if (IsCssLengthUnit(unit))
        {
            unitType = SvgLengthTypeUnknown;
            unknownUnit = InfraAscii.ToAsciiLowercase(unit);
            return true;
        }

        return false;
    }

    private static bool IsCssLengthUnit(string unit) => InfraAscii.ToAsciiLowercase(unit) is
        "q" or "rem" or "ch" or "rch" or "cap" or "rcap" or "ic" or "ric" or "lh" or "rlh" or "rex"
        or "vw" or "vh" or "vi" or "vb" or "vmin" or "vmax"
        or "svw" or "svh" or "svi" or "svb" or "svmin" or "svmax"
        or "lvw" or "lvh" or "lvi" or "lvb" or "lvmin" or "lvmax"
        or "dvw" or "dvh" or "dvi" or "dvb" or "dvmin" or "dvmax"
        or "cqw" or "cqh" or "cqi" or "cqb" or "cqmin" or "cqmax";
}

/// <summary>https://svgwg.org/svg2-draft/types.html#InterfaceSVGAngle</summary>
[IdlInterface("SVGAngle", "https://w3c.github.io/svgwg/svg2-draft/types.html#InterfaceSVGAngle")]
public sealed class SvgAngle
{
    /// <summary>The unit is not one of the predefined units.</summary>
    [IdlMember("SVG_ANGLETYPE_UNKNOWN", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGAngle__SVG_ANGLETYPE_UNKNOWN")]
    public const ushort SvgAngleTypeUnknown = 0;

    /// <summary>An angle without unit, in degrees.</summary>
    [IdlMember("SVG_ANGLETYPE_UNSPECIFIED", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGAngle__SVG_ANGLETYPE_UNSPECIFIED")]
    public const ushort SvgAngleTypeUnspecified = 1;

    /// <summary>An angle in degrees.</summary>
    [IdlMember("SVG_ANGLETYPE_DEG", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGAngle__SVG_ANGLETYPE_DEG")]
    public const ushort SvgAngleTypeDeg = 2;

    /// <summary>An angle in radians.</summary>
    [IdlMember("SVG_ANGLETYPE_RAD", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGAngle__SVG_ANGLETYPE_RAD")]
    public const ushort SvgAngleTypeRad = 3;

    /// <summary>An angle in gradians.</summary>
    [IdlMember("SVG_ANGLETYPE_GRAD", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGAngle__SVG_ANGLETYPE_GRAD")]
    public const ushort SvgAngleTypeGrad = 4;

    private static readonly string[] Units = ["", "", "deg", "rad", "grad"];

    private float _value;
    private ushort _unitType = SvgAngleTypeUnspecified;

    // "turn" has no SVG_ANGLETYPE constant
    private bool _turn;

    internal SvgAngle(ISvgValueOwner? owner = null)
    {
        Owner = owner;
    }

    internal ISvgValueOwner? Owner { get; set; }

    /// <summary>Gets the type of the unit of the angle, one of the <c>SVG_ANGLETYPE_*</c> constants.</summary>
    [IdlMember("unitType", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGAngle__unitType")]
    public ushort UnitType
    {
        get
        {
            Owner?.Sync();
            return _turn ? SvgAngleTypeUnknown : _unitType;
        }
    }

    /// <summary>Gets or sets the value of the angle, in degrees.</summary>
    [IdlMember("value", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGAngle__value")]
    public float Value
    {
        get
        {
            Owner?.Sync();
            return (float)(_value * GetDegreesFactor());
        }
        set
        {
            Owner.BeginChange();
            _value = (float)(value / GetDegreesFactor());
            Owner?.Commit();
        }
    }

    /// <summary>Gets or sets the value of the angle in its own unit.</summary>
    [IdlMember("valueInSpecifiedUnits", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGAngle__valueInSpecifiedUnits")]
    public float ValueInSpecifiedUnits
    {
        get
        {
            Owner?.Sync();
            return _value;
        }
        set
        {
            Owner.BeginChange();
            _value = value;
            Owner?.Commit();
        }
    }

    /// <summary>
    /// Gets or sets the serialized angle, e.g. <c>45deg</c>; setting an invalid angle throws a <c>SyntaxError</c>.
    /// </summary>
    [IdlMember("valueAsString", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGAngle__valueAsString")]
    public string ValueAsString
    {
        get
        {
            Owner?.Sync();
            return Serialize();
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Owner.BeginChange();
            if (!TryParse(value, out var number, out var unitType, out var turn))
                throw DomException.Syntax($"'{value}' is not a valid angle");

            _value = number;
            _unitType = unitType;
            _turn = turn;
            Owner?.Commit();
        }
    }

    /// <summary>Sets the unit type and the value in that unit of the angle.</summary>
    [IdlMember("newValueSpecifiedUnits", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGAngle__newValueSpecifiedUnits")]
    public void NewValueSpecifiedUnits(ushort unitType, float valueInSpecifiedUnits)
    {
        Owner.BeginChange();
        if (unitType is SvgAngleTypeUnknown or > SvgAngleTypeGrad)
            throw DomException.NotSupported("The unit type is not supported");

        _value = valueInSpecifiedUnits;
        _unitType = unitType;
        _turn = false;
        Owner?.Commit();
    }

    /// <summary>Converts the value of the angle to the given unit type, preserving the angle.</summary>
    [IdlMember("convertToSpecifiedUnits", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGAngle__convertToSpecifiedUnits")]
    public void ConvertToSpecifiedUnits(ushort unitType)
    {
        Owner.BeginChange();
        if (unitType is SvgAngleTypeUnknown or > SvgAngleTypeGrad)
            throw DomException.NotSupported("The unit type is not supported");

        var degrees = _value * GetDegreesFactor();
        _unitType = unitType;
        _turn = false;
        _value = (float)(degrees / GetDegreesFactor());
        Owner?.Commit();
    }

    private double GetDegreesFactor()
    {
        if (_turn)
            return 360;

        return _unitType switch
        {
            SvgAngleTypeRad => 180 / Math.PI,
            SvgAngleTypeGrad => 0.9,
            _ => 1,
        };
    }

    internal void SetRaw(float value, ushort unitType)
    {
        _value = value;
        _unitType = unitType;
        _turn = false;
    }

    internal bool TrySetRaw(string value)
    {
        if (!TryParse(value, out var number, out var unitType, out var turn))
            return false;

        _value = number;
        _unitType = unitType;
        _turn = turn;
        return true;
    }

    internal string Serialize() => SvgParsing.FormatNumber(_value) + (_turn ? "turn" : Units[_unitType]);

    internal static bool TryParse(string value, out float number, out ushort unitType, out bool turn)
    {
        number = 0;
        unitType = SvgAngleTypeUnspecified;
        turn = false;
        var position = 0;
        SvgParsing.SkipWhitespace(value, ref position);
        if (!SvgParsing.TryParseNumber(value, ref position, out var parsed) || !float.IsFinite((float)parsed))
            return false;

        var end = value.Length;
        while (end > position && InfraAscii.IsAsciiWhitespace(value[end - 1]))
        {
            end--;
        }

        number = (float)parsed;
        var unit = InfraAscii.ToAsciiLowercase(value[position..end]);
        switch (unit)
        {
            case "":
                return true;
            case "deg":
                unitType = SvgAngleTypeDeg;
                return true;
            case "rad":
                unitType = SvgAngleTypeRad;
                return true;
            case "grad":
                unitType = SvgAngleTypeGrad;
                return true;
            case "turn":
                unitType = SvgAngleTypeUnknown;
                turn = true;
                return true;
            default:
                return false;
        }
    }
}

/// <summary>https://svgwg.org/svg2-draft/coords.html#InterfaceSVGPreserveAspectRatio</summary>
[IdlInterface("SVGPreserveAspectRatio", "https://w3c.github.io/svgwg/svg2-draft/coords.html#InterfaceSVGPreserveAspectRatio")]
public sealed class SvgPreserveAspectRatio
{
    /// <summary>The alignment is not one of the predefined values.</summary>
    [IdlMember("SVG_PRESERVEASPECTRATIO_UNKNOWN", "https://w3c.github.io/svgwg/svg2-draft/coords.html#__svg__SVGPreserveAspectRatio__SVG_PRESERVEASPECTRATIO_UNKNOWN")]
    public const ushort SvgPreserveAspectRatioUnknown = 0;

    /// <summary>No uniform scaling: the <c>none</c> alignment.</summary>
    [IdlMember("SVG_PRESERVEASPECTRATIO_NONE", "https://w3c.github.io/svgwg/svg2-draft/coords.html#__svg__SVGPreserveAspectRatio__SVG_PRESERVEASPECTRATIO_NONE")]
    public const ushort SvgPreserveAspectRatioNone = 1;

    /// <summary>The <c>xMinYMin</c> alignment.</summary>
    [IdlMember("SVG_PRESERVEASPECTRATIO_XMINYMIN", "https://w3c.github.io/svgwg/svg2-draft/coords.html#__svg__SVGPreserveAspectRatio__SVG_PRESERVEASPECTRATIO_XMINYMIN")]
    public const ushort SvgPreserveAspectRatioXMinYMin = 2;

    /// <summary>The <c>xMidYMin</c> alignment.</summary>
    [IdlMember("SVG_PRESERVEASPECTRATIO_XMIDYMIN", "https://w3c.github.io/svgwg/svg2-draft/coords.html#__svg__SVGPreserveAspectRatio__SVG_PRESERVEASPECTRATIO_XMIDYMIN")]
    public const ushort SvgPreserveAspectRatioXMidYMin = 3;

    /// <summary>The <c>xMaxYMin</c> alignment.</summary>
    [IdlMember("SVG_PRESERVEASPECTRATIO_XMAXYMIN", "https://w3c.github.io/svgwg/svg2-draft/coords.html#__svg__SVGPreserveAspectRatio__SVG_PRESERVEASPECTRATIO_XMAXYMIN")]
    public const ushort SvgPreserveAspectRatioXMaxYMin = 4;

    /// <summary>The <c>xMinYMid</c> alignment.</summary>
    [IdlMember("SVG_PRESERVEASPECTRATIO_XMINYMID", "https://w3c.github.io/svgwg/svg2-draft/coords.html#__svg__SVGPreserveAspectRatio__SVG_PRESERVEASPECTRATIO_XMINYMID")]
    public const ushort SvgPreserveAspectRatioXMinYMid = 5;

    /// <summary>The <c>xMidYMid</c> alignment.</summary>
    [IdlMember("SVG_PRESERVEASPECTRATIO_XMIDYMID", "https://w3c.github.io/svgwg/svg2-draft/coords.html#__svg__SVGPreserveAspectRatio__SVG_PRESERVEASPECTRATIO_XMIDYMID")]
    public const ushort SvgPreserveAspectRatioXMidYMid = 6;

    /// <summary>The <c>xMaxYMid</c> alignment.</summary>
    [IdlMember("SVG_PRESERVEASPECTRATIO_XMAXYMID", "https://w3c.github.io/svgwg/svg2-draft/coords.html#__svg__SVGPreserveAspectRatio__SVG_PRESERVEASPECTRATIO_XMAXYMID")]
    public const ushort SvgPreserveAspectRatioXMaxYMid = 7;

    /// <summary>The <c>xMinYMax</c> alignment.</summary>
    [IdlMember("SVG_PRESERVEASPECTRATIO_XMINYMAX", "https://w3c.github.io/svgwg/svg2-draft/coords.html#__svg__SVGPreserveAspectRatio__SVG_PRESERVEASPECTRATIO_XMINYMAX")]
    public const ushort SvgPreserveAspectRatioXMinYMax = 8;

    /// <summary>The <c>xMidYMax</c> alignment.</summary>
    [IdlMember("SVG_PRESERVEASPECTRATIO_XMIDYMAX", "https://w3c.github.io/svgwg/svg2-draft/coords.html#__svg__SVGPreserveAspectRatio__SVG_PRESERVEASPECTRATIO_XMIDYMAX")]
    public const ushort SvgPreserveAspectRatioXMidYMax = 9;

    /// <summary>The <c>xMaxYMax</c> alignment.</summary>
    [IdlMember("SVG_PRESERVEASPECTRATIO_XMAXYMAX", "https://w3c.github.io/svgwg/svg2-draft/coords.html#__svg__SVGPreserveAspectRatio__SVG_PRESERVEASPECTRATIO_XMAXYMAX")]
    public const ushort SvgPreserveAspectRatioXMaxYMax = 10;

    /// <summary>The meet-or-slice value is not one of the predefined values.</summary>
    [IdlMember("SVG_MEETORSLICE_UNKNOWN", "https://w3c.github.io/svgwg/svg2-draft/coords.html#__svg__SVGPreserveAspectRatio__SVG_MEETORSLICE_UNKNOWN")]
    public const ushort SvgMeetOrSliceUnknown = 0;

    /// <summary>The <c>meet</c> value: the viewBox is scaled to fit entirely in the viewport.</summary>
    [IdlMember("SVG_MEETORSLICE_MEET", "https://w3c.github.io/svgwg/svg2-draft/coords.html#__svg__SVGPreserveAspectRatio__SVG_MEETORSLICE_MEET")]
    public const ushort SvgMeetOrSliceMeet = 1;

    /// <summary>The <c>slice</c> value: the viewBox is scaled to cover the entire viewport.</summary>
    [IdlMember("SVG_MEETORSLICE_SLICE", "https://w3c.github.io/svgwg/svg2-draft/coords.html#__svg__SVGPreserveAspectRatio__SVG_MEETORSLICE_SLICE")]
    public const ushort SvgMeetOrSliceSlice = 2;

    private static readonly string[] Aligns = ["", "none", "xMinYMin", "xMidYMin", "xMaxYMin", "xMinYMid", "xMidYMid", "xMaxYMid", "xMinYMax", "xMidYMax", "xMaxYMax"];

    private ushort _align = SvgPreserveAspectRatioXMidYMid;
    private ushort _meetOrSlice = SvgMeetOrSliceMeet;

    internal SvgPreserveAspectRatio(ISvgValueOwner? owner = null)
    {
        Owner = owner;
    }

    internal ISvgValueOwner? Owner { get; }

    /// <summary>Gets or sets the alignment, one of the <c>SVG_PRESERVEASPECTRATIO_*</c> constants.</summary>
    [IdlMember("align", "https://w3c.github.io/svgwg/svg2-draft/coords.html#__svg__SVGPreserveAspectRatio__align")]
    public ushort Align
    {
        get
        {
            Owner?.Sync();
            return _align;
        }
        set
        {
            Owner.BeginChange();
            if (value is SvgPreserveAspectRatioUnknown or > SvgPreserveAspectRatioXMaxYMax)
                throw new TypeErrorException("The value is not a valid alignment");

            _align = value;
            Owner?.Commit();
        }
    }

    /// <summary>
    /// Gets or sets whether the viewBox is fitted (<c>meet</c>) or covers the viewport (<c>slice</c>), one of the
    /// <c>SVG_MEETORSLICE_*</c> constants.
    /// </summary>
    [IdlMember("meetOrSlice", "https://w3c.github.io/svgwg/svg2-draft/coords.html#__svg__SVGPreserveAspectRatio__meetOrSlice")]
    public ushort MeetOrSlice
    {
        get
        {
            Owner?.Sync();
            return _meetOrSlice;
        }
        set
        {
            Owner.BeginChange();
            if (value is SvgMeetOrSliceUnknown or > SvgMeetOrSliceSlice)
                throw new TypeErrorException("The value is not a valid meetOrSlice value");

            _meetOrSlice = value;
            Owner?.Commit();
        }
    }

    internal void SetRaw(ushort align, ushort meetOrSlice)
    {
        _align = align;
        _meetOrSlice = meetOrSlice;
    }

    internal string Serialize() => Aligns[_align] + (_meetOrSlice == SvgMeetOrSliceSlice ? " slice" : "");

    /// <summary>https://svgwg.org/svg2-draft/coords.html#PreserveAspectRatioAttribute</summary>
    internal static bool TryParse(string? value, out ushort align, out ushort meetOrSlice)
    {
        align = SvgPreserveAspectRatioXMidYMid;
        meetOrSlice = SvgMeetOrSliceMeet;
        if (value is null)
            return false;

        var parts = SvgParsing.SplitList(value, commaSeparated: false);
        if (parts.Count is 0 or > 2)
            return false;

        var index = Array.IndexOf(Aligns, parts[0]);
        if (index <= 0)
            return false;

        align = (ushort)index;
        if (parts.Count == 2)
        {
            if (parts[1] == "meet")
            {
                meetOrSlice = SvgMeetOrSliceMeet;
            }
            else if (parts[1] == "slice")
            {
                meetOrSlice = SvgMeetOrSliceSlice;
            }
            else
            {
                return false;
            }
        }

        return true;
    }
}
