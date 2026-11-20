using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The <c>ConstrainULong</c> typedef (https://w3c.github.io/mediacapture-main/#dom-constrainulong): a bare value, or a range with an exact and an ideal value.</summary>
public readonly struct ConstrainULong : IUnionValue, IEquatable<ConstrainULong>
{
    private ConstrainULong(object value) => Value = value;

    /// <summary>Gets the value of the union: <see cref="uint"/> or <see cref="ConstrainULongRange"/>.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Gets the bare value when the union holds it, otherwise null.</summary>
    [NotInIdl]
    public uint? Bare => Value as uint?;

    /// <summary>Gets the range when the union holds it, otherwise null.</summary>
    [NotInIdl]
    public ConstrainULongRange? Range => Value as ConstrainULongRange;

    /// <summary>Creates the union from the bare value.</summary>
    [NotInIdl]
    public static implicit operator ConstrainULong(uint value) => new(value);

    /// <summary>Creates the union from the bare value.</summary>
    [NotInIdl]
    public static ConstrainULong FromUInt32(uint value) => value;

    /// <summary>Creates the union from the range.</summary>
    [NotInIdl]
    public static implicit operator ConstrainULong(ConstrainULongRange value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates the union from the range.</summary>
    [NotInIdl]
    public static ConstrainULong FromConstrainULongRange(ConstrainULongRange value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(ConstrainULong other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is ConstrainULong other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <summary>Returns whether the two unions hold the same value.</summary>
    [NotInIdl]
    public static bool operator ==(ConstrainULong left, ConstrainULong right) => left.Equals(right);

    /// <summary>Returns whether the two unions hold different values.</summary>
    [NotInIdl]
    public static bool operator !=(ConstrainULong left, ConstrainULong right) => !left.Equals(right);
}

/// <summary>The <c>ConstrainDouble</c> typedef (https://w3c.github.io/mediacapture-main/#dom-constraindouble): a bare value, or a range with an exact and an ideal value.</summary>
public readonly struct ConstrainDouble : IUnionValue, IEquatable<ConstrainDouble>
{
    private ConstrainDouble(object value) => Value = value;

    /// <summary>Gets the value of the union: <see cref="double"/> or <see cref="ConstrainDoubleRange"/>.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Gets the bare value when the union holds it, otherwise null.</summary>
    [NotInIdl]
    public double? Bare => Value as double?;

    /// <summary>Gets the range when the union holds it, otherwise null.</summary>
    [NotInIdl]
    public ConstrainDoubleRange? Range => Value as ConstrainDoubleRange;

    /// <summary>Creates the union from the bare value.</summary>
    [NotInIdl]
    public static implicit operator ConstrainDouble(double value) => new(value);

    /// <summary>Creates the union from the bare value.</summary>
    [NotInIdl]
    public static ConstrainDouble FromDouble(double value) => value;

    /// <summary>Creates the union from the range.</summary>
    [NotInIdl]
    public static implicit operator ConstrainDouble(ConstrainDoubleRange value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates the union from the range.</summary>
    [NotInIdl]
    public static ConstrainDouble FromConstrainDoubleRange(ConstrainDoubleRange value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(ConstrainDouble other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is ConstrainDouble other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <summary>Returns whether the two unions hold the same value.</summary>
    [NotInIdl]
    public static bool operator ==(ConstrainDouble left, ConstrainDouble right) => left.Equals(right);

    /// <summary>Returns whether the two unions hold different values.</summary>
    [NotInIdl]
    public static bool operator !=(ConstrainDouble left, ConstrainDouble right) => !left.Equals(right);
}

/// <summary>The <c>ConstrainBoolean</c> typedef (https://w3c.github.io/mediacapture-main/#dom-constrainboolean): a bare value, or an exact and an ideal value.</summary>
public readonly struct ConstrainBoolean : IUnionValue, IEquatable<ConstrainBoolean>
{
    private ConstrainBoolean(object value) => Value = value;

    /// <summary>Gets the value of the union: <see cref="bool"/> or <see cref="ConstrainBooleanParameters"/>.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Gets the bare value when the union holds it, otherwise null.</summary>
    [NotInIdl]
    public bool? Bare => Value as bool?;

    /// <summary>Gets the parameters when the union holds it, otherwise null.</summary>
    [NotInIdl]
    public ConstrainBooleanParameters? Parameters => Value as ConstrainBooleanParameters;

    /// <summary>Creates the union from the bare value.</summary>
    [NotInIdl]
    public static implicit operator ConstrainBoolean(bool value) => new(value);

    /// <summary>Creates the union from the bare value.</summary>
    [NotInIdl]
    public static ConstrainBoolean FromBoolean(bool value) => value;

    /// <summary>Creates the union from the parameters.</summary>
    [NotInIdl]
    public static implicit operator ConstrainBoolean(ConstrainBooleanParameters value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates the union from the parameters.</summary>
    [NotInIdl]
    public static ConstrainBoolean FromConstrainBooleanParameters(ConstrainBooleanParameters value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(ConstrainBoolean other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is ConstrainBoolean other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <summary>Returns whether the two unions hold the same value.</summary>
    [NotInIdl]
    public static bool operator ==(ConstrainBoolean left, ConstrainBoolean right) => left.Equals(right);

    /// <summary>Returns whether the two unions hold different values.</summary>
    [NotInIdl]
    public static bool operator !=(ConstrainBoolean left, ConstrainBoolean right) => !left.Equals(right);
}

/// <summary>The <c>ConstrainDOMString</c> typedef (https://w3c.github.io/mediacapture-main/#dom-constraindomstring): a bare value, a list of bare values, or exact and ideal values.</summary>
public readonly struct ConstrainDomString : IUnionValue, IEquatable<ConstrainDomString>
{
    private ConstrainDomString(object value) => Value = value;

    /// <summary>Gets the value of the union: <see cref="string"/> or <c>string[]</c> or <see cref="ConstrainDomStringParameters"/>.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Gets the bare value when the union holds it, otherwise null.</summary>
    [NotInIdl]
    public string? Bare => Value as string;

    /// <summary>Gets the list of bare values when the union holds it, otherwise null.</summary>
    [NotInIdl]
    public IReadOnlyList<string>? BareList => Value as string[];

    /// <summary>Gets the parameters when the union holds it, otherwise null.</summary>
    [NotInIdl]
    public ConstrainDomStringParameters? Parameters => Value as ConstrainDomStringParameters;

    /// <summary>Creates the union from the bare value.</summary>
    [NotInIdl]
    public static implicit operator ConstrainDomString(string value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates the union from the bare value.</summary>
    [NotInIdl]
    public static ConstrainDomString FromString(string value) => value;

    /// <summary>Creates the union from the list of bare values.</summary>
    [NotInIdl]
    public static implicit operator ConstrainDomString(string[] value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates the union from the list of bare values.</summary>
    [NotInIdl]
    public static ConstrainDomString FromStrings(string[] value) => value;

    /// <summary>Creates the union from the parameters.</summary>
    [NotInIdl]
    public static implicit operator ConstrainDomString(ConstrainDomStringParameters value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates the union from the parameters.</summary>
    [NotInIdl]
    public static ConstrainDomString FromConstrainDomStringParameters(ConstrainDomStringParameters value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(ConstrainDomString other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is ConstrainDomString other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <summary>Returns whether the two unions hold the same value.</summary>
    [NotInIdl]
    public static bool operator ==(ConstrainDomString left, ConstrainDomString right) => left.Equals(right);

    /// <summary>Returns whether the two unions hold different values.</summary>
    [NotInIdl]
    public static bool operator !=(ConstrainDomString left, ConstrainDomString right) => !left.Equals(right);
}

/// <summary>The <c>ConstrainBooleanOrDOMString</c> typedef (https://w3c.github.io/mediacapture-main/#dom-constrainbooleanordomstring): a bare value, or an exact and an ideal value.</summary>
public readonly struct ConstrainBooleanOrDomString : IUnionValue, IEquatable<ConstrainBooleanOrDomString>
{
    private ConstrainBooleanOrDomString(object value) => Value = value;

    /// <summary>Gets the value of the union: <see cref="bool"/> or <see cref="string"/> or <see cref="ConstrainBooleanOrDomStringParameters"/>.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Gets the bare boolean value when the union holds it, otherwise null.</summary>
    [NotInIdl]
    public bool? BareFlag => Value as bool?;

    /// <summary>Gets the bare string value when the union holds it, otherwise null.</summary>
    [NotInIdl]
    public string? BareText => Value as string;

    /// <summary>Gets the parameters when the union holds it, otherwise null.</summary>
    [NotInIdl]
    public ConstrainBooleanOrDomStringParameters? Parameters => Value as ConstrainBooleanOrDomStringParameters;

    /// <summary>Creates the union from the bare boolean value.</summary>
    [NotInIdl]
    public static implicit operator ConstrainBooleanOrDomString(bool value) => new(value);

    /// <summary>Creates the union from the bare boolean value.</summary>
    [NotInIdl]
    public static ConstrainBooleanOrDomString FromBoolean(bool value) => value;

    /// <summary>Creates the union from the bare string value.</summary>
    [NotInIdl]
    public static implicit operator ConstrainBooleanOrDomString(string value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates the union from the bare string value.</summary>
    [NotInIdl]
    public static ConstrainBooleanOrDomString FromString(string value) => value;

    /// <summary>Creates the union from the parameters.</summary>
    [NotInIdl]
    public static implicit operator ConstrainBooleanOrDomString(ConstrainBooleanOrDomStringParameters value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates the union from the parameters.</summary>
    [NotInIdl]
    public static ConstrainBooleanOrDomString FromConstrainBooleanOrDomStringParameters(ConstrainBooleanOrDomStringParameters value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(ConstrainBooleanOrDomString other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is ConstrainBooleanOrDomString other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <summary>Returns whether the two unions hold the same value.</summary>
    [NotInIdl]
    public static bool operator ==(ConstrainBooleanOrDomString left, ConstrainBooleanOrDomString right) => left.Equals(right);

    /// <summary>Returns whether the two unions hold different values.</summary>
    [NotInIdl]
    public static bool operator !=(ConstrainBooleanOrDomString left, ConstrainBooleanOrDomString right) => !left.Equals(right);
}

/// <summary>The <c>ConstrainPoint2D</c> typedef (https://w3c.github.io/mediacapture-image/#typedefdef-constrainpoint2d): a bare list of points, or an exact and an ideal list of points.</summary>
public readonly struct ConstrainPoint2D : IUnionValue, IEquatable<ConstrainPoint2D>
{
    private ConstrainPoint2D(object value) => Value = value;

    /// <summary>Gets the value of the union: <c>Point2D[]</c> or <see cref="ConstrainPoint2DParameters"/>.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Gets the bare list of points when the union holds it, otherwise null.</summary>
    [NotInIdl]
    public IReadOnlyList<Point2D>? Bare => Value as Point2D[];

    /// <summary>Gets the parameters when the union holds it, otherwise null.</summary>
    [NotInIdl]
    public ConstrainPoint2DParameters? Parameters => Value as ConstrainPoint2DParameters;

    /// <summary>Creates the union from the bare list of points.</summary>
    [NotInIdl]
    public static implicit operator ConstrainPoint2D(Point2D[] value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates the union from the bare list of points.</summary>
    [NotInIdl]
    public static ConstrainPoint2D FromPoints(Point2D[] value) => value;

    /// <summary>Creates the union from the parameters.</summary>
    [NotInIdl]
    public static implicit operator ConstrainPoint2D(ConstrainPoint2DParameters value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates the union from the parameters.</summary>
    [NotInIdl]
    public static ConstrainPoint2D FromConstrainPoint2DParameters(ConstrainPoint2DParameters value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(ConstrainPoint2D other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is ConstrainPoint2D other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <summary>Returns whether the two unions hold the same value.</summary>
    [NotInIdl]
    public static bool operator ==(ConstrainPoint2D left, ConstrainPoint2D right) => left.Equals(right);

    /// <summary>Returns whether the two unions hold different values.</summary>
    [NotInIdl]
    public static bool operator !=(ConstrainPoint2D left, ConstrainPoint2D right) => !left.Equals(right);
}

/// <summary>The <c>(boolean or ConstrainDouble)</c> union of the pan, tilt and zoom constraints (https://w3c.github.io/mediacapture-image/#dom-mediatrackconstraintset-pan): a boolean (whether the property is requested), a bare value, or a range.</summary>
public readonly struct BooleanOrConstrainDouble : IUnionValue, IEquatable<BooleanOrConstrainDouble>
{
    private BooleanOrConstrainDouble(object value) => Value = value;

    /// <summary>Gets the value of the union: <see cref="bool"/> or <see cref="double"/> or <see cref="ConstrainDoubleRange"/>.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Gets the boolean when the union holds it, otherwise null.</summary>
    [NotInIdl]
    public bool? Flag => Value as bool?;

    /// <summary>Gets the bare value when the union holds it, otherwise null.</summary>
    [NotInIdl]
    public double? Bare => Value as double?;

    /// <summary>Gets the range when the union holds it, otherwise null.</summary>
    [NotInIdl]
    public ConstrainDoubleRange? Range => Value as ConstrainDoubleRange;

    /// <summary>Creates the union from the boolean.</summary>
    [NotInIdl]
    public static implicit operator BooleanOrConstrainDouble(bool value) => new(value);

    /// <summary>Creates the union from the boolean.</summary>
    [NotInIdl]
    public static BooleanOrConstrainDouble FromBoolean(bool value) => value;

    /// <summary>Creates the union from the bare value.</summary>
    [NotInIdl]
    public static implicit operator BooleanOrConstrainDouble(double value) => new(value);

    /// <summary>Creates the union from the bare value.</summary>
    [NotInIdl]
    public static BooleanOrConstrainDouble FromDouble(double value) => value;

    /// <summary>Creates the union from the range.</summary>
    [NotInIdl]
    public static implicit operator BooleanOrConstrainDouble(ConstrainDoubleRange value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates the union from the range.</summary>
    [NotInIdl]
    public static BooleanOrConstrainDouble FromConstrainDoubleRange(ConstrainDoubleRange value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(BooleanOrConstrainDouble other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is BooleanOrConstrainDouble other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <summary>Returns whether the two unions hold the same value.</summary>
    [NotInIdl]
    public static bool operator ==(BooleanOrConstrainDouble left, BooleanOrConstrainDouble right) => left.Equals(right);

    /// <summary>Returns whether the two unions hold different values.</summary>
    [NotInIdl]
    public static bool operator !=(BooleanOrConstrainDouble left, BooleanOrConstrainDouble right) => !left.Equals(right);
}

/// <summary>The <c>(DOMString or sequence&lt;DOMString&gt;)</c> union of the exact and ideal values of a <see cref="ConstrainDomStringParameters"/>: a value, or a list of values.</summary>
public readonly struct StringOrStringList : IUnionValue, IEquatable<StringOrStringList>
{
    private StringOrStringList(object value) => Value = value;

    /// <summary>Gets the value of the union: <see cref="string"/> or <c>string[]</c>.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Gets the value when the union holds it, otherwise null.</summary>
    [NotInIdl]
    public string? One => Value as string;

    /// <summary>Gets the list of values when the union holds it, otherwise null.</summary>
    [NotInIdl]
    public IReadOnlyList<string>? Many => Value as string[];

    /// <summary>Creates the union from the value.</summary>
    [NotInIdl]
    public static implicit operator StringOrStringList(string value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates the union from the value.</summary>
    [NotInIdl]
    public static StringOrStringList FromString(string value) => value;

    /// <summary>Creates the union from the list of values.</summary>
    [NotInIdl]
    public static implicit operator StringOrStringList(string[] value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates the union from the list of values.</summary>
    [NotInIdl]
    public static StringOrStringList FromStrings(string[] value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(StringOrStringList other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is StringOrStringList other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <summary>Returns whether the two unions hold the same value.</summary>
    [NotInIdl]
    public static bool operator ==(StringOrStringList left, StringOrStringList right) => left.Equals(right);

    /// <summary>Returns whether the two unions hold different values.</summary>
    [NotInIdl]
    public static bool operator !=(StringOrStringList left, StringOrStringList right) => !left.Equals(right);
}

/// <summary>The <c>(boolean or DOMString)</c> union of the <c>echoCancellation</c> constrainable property: a boolean, or an echo cancellation mode (<c>all</c>, <c>remote-only</c>).</summary>
public readonly struct BooleanOrString : IUnionValue, IEquatable<BooleanOrString>
{
    private BooleanOrString(object value) => Value = value;

    /// <summary>Gets the value of the union: <see cref="bool"/> or <see cref="string"/>.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Gets the boolean when the union holds it, otherwise null.</summary>
    [NotInIdl]
    public bool? Flag => Value as bool?;

    /// <summary>Gets the string when the union holds it, otherwise null.</summary>
    [NotInIdl]
    public string? Text => Value as string;

    /// <summary>Creates the union from the boolean.</summary>
    [NotInIdl]
    public static implicit operator BooleanOrString(bool value) => new(value);

    /// <summary>Creates the union from the boolean.</summary>
    [NotInIdl]
    public static BooleanOrString FromBoolean(bool value) => value;

    /// <summary>Creates the union from the string.</summary>
    [NotInIdl]
    public static implicit operator BooleanOrString(string value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates the union from the string.</summary>
    [NotInIdl]
    public static BooleanOrString FromString(string value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(BooleanOrString other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is BooleanOrString other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <summary>Returns whether the two unions hold the same value.</summary>
    [NotInIdl]
    public static bool operator ==(BooleanOrString left, BooleanOrString right) => left.Equals(right);

    /// <summary>Returns whether the two unions hold different values.</summary>
    [NotInIdl]
    public static bool operator !=(BooleanOrString left, BooleanOrString right) => !left.Equals(right);
}

/// <summary>The <c>(boolean or MediaTrackConstraints)</c> union of the <c>audio</c> and <c>video</c> members of <see cref="MediaStreamConstraints"/> and <see cref="DisplayMediaStreamOptions"/>: whether the kind of media is requested, or the constraints of its track.</summary>
public readonly struct BooleanOrMediaTrackConstraints : IUnionValue, IEquatable<BooleanOrMediaTrackConstraints>
{
    private BooleanOrMediaTrackConstraints(object value) => Value = value;

    /// <summary>Gets the value of the union: <see cref="bool"/> or <see cref="MediaTrackConstraints"/>.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Gets the boolean when the union holds it, otherwise null.</summary>
    [NotInIdl]
    public bool? Flag => Value as bool?;

    /// <summary>Gets the constraints when the union holds it, otherwise null.</summary>
    [NotInIdl]
    public MediaTrackConstraints? Constraints => Value as MediaTrackConstraints;

    /// <summary>Creates the union from the boolean.</summary>
    [NotInIdl]
    public static implicit operator BooleanOrMediaTrackConstraints(bool value) => new(value);

    /// <summary>Creates the union from the boolean.</summary>
    [NotInIdl]
    public static BooleanOrMediaTrackConstraints FromBoolean(bool value) => value;

    /// <summary>Creates the union from the constraints.</summary>
    [NotInIdl]
    public static implicit operator BooleanOrMediaTrackConstraints(MediaTrackConstraints value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates the union from the constraints.</summary>
    [NotInIdl]
    public static BooleanOrMediaTrackConstraints FromMediaTrackConstraints(MediaTrackConstraints value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(BooleanOrMediaTrackConstraints other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is BooleanOrMediaTrackConstraints other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <summary>Returns whether the two unions hold the same value.</summary>
    [NotInIdl]
    public static bool operator ==(BooleanOrMediaTrackConstraints left, BooleanOrMediaTrackConstraints right) => left.Equals(right);

    /// <summary>Returns whether the two unions hold different values.</summary>
    [NotInIdl]
    public static bool operator !=(BooleanOrMediaTrackConstraints left, BooleanOrMediaTrackConstraints right) => !left.Equals(right);
}
