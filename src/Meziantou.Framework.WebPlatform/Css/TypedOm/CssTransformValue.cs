using System.Collections;
using System.Text;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.TypedOm;

/// <summary>A &lt;transform-list&gt; value (https://drafts.css-houdini.org/css-typed-om-1/#csstransformvalue): a list of transform components.</summary>
[IdlInterface("CSSTransformValue", "https://drafts.css-houdini.org/css-typed-om-1/#csstransformvalue")]
public sealed class CssTransformValue : CssStyleValue, IReadOnlyList<CssTransformComponent>
{
    private readonly List<CssTransformComponent> _values;

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-csstransformvalue-csstransformvalue</summary>
    [IdlMember("constructor", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csstransformvalue-csstransformvalue")]
    public CssTransformValue(IEnumerable<CssTransformComponent> transforms)
    {
        ArgumentNullException.ThrowIfNull(transforms);
        _values = [.. transforms];
        if (_values.Count == 0)
            throw new TypeErrorException("A transform value must contain at least one transform component");

        foreach (var value in _values)
        {
            ArgumentNullException.ThrowIfNull(value, nameof(transforms));
        }
    }

    /// <summary>Gets the number of transform components.</summary>
    [IdlMember("length", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csstransformvalue-length")]
    public uint Length => (uint)_values.Count;

    /// <summary>
    /// Gets or sets a transform component (https://drafts.css-houdini.org/css-typed-om-1/#dom-csstransformvalue-__getter__-index-index):
    /// setting the index that follows the last component appends a component, a greater index throws a RangeError.
    /// </summary>
    [IdlMember("getter", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csstransformvalue-__getter__-index-index")]
    [IdlMember("setter", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csstransformvalue-__setter__-index-val-val")]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public CssTransformComponent this[uint index]
    {
        get => _values[(int)index];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (index < (uint)_values.Count)
            {
                _values[(int)index] = value;
            }
            else if (index == (uint)_values.Count)
            {
                _values.Add(value);
            }
            else
            {
                throw new RangeErrorException("The index is greater than the number of transform components");
            }
        }
    }

    /// <summary>Gets the transform component at the given index.</summary>
    [NotInIdl]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public CssTransformComponent this[int index] => _values[index];

    /// <summary>Gets the number of transform components.</summary>
    [NotInIdl]
    public int Count => _values.Count;

    /// <summary>Whether all the components are 2D (https://drafts.css-houdini.org/css-typed-om-1/#dom-csstransformvalue-is2d).</summary>
    [IdlMember("is2D", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csstransformvalue-is2d")]
    public bool Is2D => _values.TrueForAll(value => value.Is2D);

    /// <summary>Returns the product of the matrices of the components (https://drafts.css-houdini.org/css-typed-om-1/#dom-csstransformvalue-tomatrix).</summary>
    [IdlMember("toMatrix", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csstransformvalue-tomatrix")]
    public DomMatrix ToMatrix()
    {
        var matrix = new DomMatrix();
        foreach (var value in _values)
        {
            var componentMatrix = value.ToMatrix();
            matrix = DomMatrix.Create(TransformMatrices.Multiply(matrix.Elements, componentMatrix.Elements), matrix.Is2DMatrix && componentMatrix.Is2DMatrix);
        }

        return matrix;
    }

    /// <inheritdoc/>
    [NotInIdl]
    public IEnumerator<CssTransformComponent> GetEnumerator() => _values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#serialize-a-csstransformvalue: the components separated by spaces.</summary>
    [NotInIdl("The stringifier of CSSStyleValue")]
    public override string ToString() => string.Join(' ', _values);
}

/// <summary>A &lt;transform-function&gt; (https://drafts.css-houdini.org/css-typed-om-1/#csstransformcomponent).</summary>
[IdlInterface("CSSTransformComponent", "https://drafts.css-houdini.org/css-typed-om-1/#csstransformcomponent")]
public abstract class CssTransformComponent
{
    private bool _is2D;

    private protected CssTransformComponent(bool is2D)
    {
        _is2D = is2D;
    }

    /// <summary>
    /// Whether the component is a 2D transform (https://drafts.css-houdini.org/css-typed-om-1/#dom-csstransformcomponent-is2d).
    /// Setting it does nothing for the skews (always 2D) and the perspective (always 3D).
    /// </summary>
    [IdlMember("is2D", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csstransformcomponent-is2d")]
    public bool Is2D
    {
        get => _is2D;
        set
        {
            if (CanChangeIs2D)
            {
                _is2D = value;
            }
        }
    }

    private protected virtual bool CanChangeIs2D => true;

    /// <summary>
    /// Returns the equivalent 4x4 matrix of the component (https://drafts.css-houdini.org/css-typed-om-1/#dom-csstransformcomponent-tomatrix).
    /// The lengths must be compatible with px (a relative length or a percentage throws a TypeError).
    /// </summary>
    [IdlMember("toMatrix", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csstransformcomponent-tomatrix")]
    public DomMatrix ToMatrix() => DomMatrix.Create(GetMatrix(), Is2D);

    /// <summary>Returns the matrix of the component, in column-major order.</summary>
    private protected abstract double[] GetMatrix();

    /// <summary>The serialization of the component (https://drafts.css-houdini.org/css-typed-om-1/#transformvalue-serialization).</summary>
    [NotInIdl("The stringifier of CSSTransformComponent")]
    public abstract override string ToString();

    /// <summary>Serializes a function whose arguments are separated by commas.</summary>
    private protected static string SerializeFunction(string name, params ReadOnlySpan<CssNumericValue> arguments)
    {
        var builder = new StringBuilder(name).Append('(');
        for (var i = 0; i < arguments.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(arguments[i].ToString());
        }

        return builder.Append(')').ToString();
    }

    /// <summary>Throws a TypeError when the value does not match the production (https://drafts.css-houdini.org/css-typed-om-1/#cssnumericvalue-match).</summary>
    private protected static T Check<T>(T value, CssNumericBaseType? baseType, bool allowPercentages = false, string? name = null)
        where T : CssNumericValue
    {
        ArgumentNullException.ThrowIfNull(value, name);
        var type = value.NumericType;
        var matches = baseType is { } expected
            ? (allowPercentages ? type.MatchesWithPercentage(expected) : type.Matches(expected))
            : type.IsNumber;
        if (!matches)
            throw new TypeErrorException($"The value '{value}' does not match the expected type");

        return value;
    }

    /// <summary>Converts a value to a unit (e.g. a length to px for the matrices), or throws a TypeError.</summary>
    private protected static double ToUnit(CssNumericValue value, string unit) => value.To(unit).Value;
}

/// <summary>translate(), translateX(), translateY(), translateZ() and translate3d() (https://drafts.css-houdini.org/css-typed-om-1/#csstranslate).</summary>
[IdlInterface("CSSTranslate", "https://drafts.css-houdini.org/css-typed-om-1/#csstranslate")]
public sealed class CssTranslate : CssTransformComponent
{
    private CssNumericValue _x;
    private CssNumericValue _y;
    private CssNumericValue _z;

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-csstranslate-csstranslate</summary>
    [IdlMember("constructor", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csstranslate-csstranslate")]
    public CssTranslate(CssNumericValue x, CssNumericValue y, CssNumericValue? z = null)
        : base(is2D: z is null)
    {
        _x = Check(x, CssNumericBaseType.Length, allowPercentages: true);
        _y = Check(y, CssNumericBaseType.Length, allowPercentages: true);
        _z = z is null ? new CssUnitValue(0, "px") : Check(z, CssNumericBaseType.Length);
    }

    /// <summary>Gets or sets the translation along the X axis.</summary>
    [IdlMember("x", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csstranslate-x")]
    public CssNumericValue X
    {
        get => _x;
        set => _x = Check(value, CssNumericBaseType.Length, allowPercentages: true);
    }

    /// <summary>Gets or sets the translation along the Y axis.</summary>
    [IdlMember("y", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csstranslate-y")]
    public CssNumericValue Y
    {
        get => _y;
        set => _y = Check(value, CssNumericBaseType.Length, allowPercentages: true);
    }

    /// <summary>Gets or sets the translation along the Z axis.</summary>
    [IdlMember("z", "https://drafts.css-houdini.org/css-typed-om-1/#dom-csstranslate-z")]
    public CssNumericValue Z
    {
        get => _z;
        set => _z = Check(value, CssNumericBaseType.Length);
    }

    private protected override double[] GetMatrix()
    {
        var x = ToUnit(_x, "px");
        var y = ToUnit(_y, "px");
        var z = Is2D ? 0 : ToUnit(_z, "px");
        return [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, x, y, z, 1];
    }

    /// <inheritdoc/>
    [NotInIdl("The stringifier of CSSTransformComponent")]
    public override string ToString() => Is2D ? SerializeFunction("translate", _x, _y) : SerializeFunction("translate3d", _x, _y, _z);
}

/// <summary>rotate(), rotateX(), rotateY(), rotateZ() and rotate3d() (https://drafts.css-houdini.org/css-typed-om-1/#cssrotate).</summary>
[IdlInterface("CSSRotate", "https://drafts.css-houdini.org/css-typed-om-1/#cssrotate")]
public sealed class CssRotate : CssTransformComponent
{
    private CssNumericValue _x;
    private CssNumericValue _y;
    private CssNumericValue _z;
    private CssNumericValue _angle;

    /// <summary>A 2D rotation (https://drafts.css-houdini.org/css-typed-om-1/#dom-cssrotate-cssrotate).</summary>
    [IdlMember("constructor", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssrotate-cssrotate")]
    public CssRotate(CssNumericValue angle)
        : base(is2D: true)
    {
        _angle = Check(angle, CssNumericBaseType.Angle);
        _x = new CssUnitValue(0, "number");
        _y = new CssUnitValue(0, "number");
        _z = new CssUnitValue(1, "number");
    }

    /// <summary>A 3D rotation around the axis (x, y, z) (https://drafts.css-houdini.org/css-typed-om-1/#dom-cssrotate-cssrotate-x-y-z-angle).</summary>
    [IdlMember("constructor", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssrotate-cssrotate-x-y-z-angle")]
    public CssRotate(CssNumberish x, CssNumberish y, CssNumberish z, CssNumericValue angle)
        : base(is2D: false)
    {
        _angle = Check(angle, CssNumericBaseType.Angle);
        _x = Check(x.Rectify(), baseType: null);
        _y = Check(y.Rectify(), baseType: null);
        _z = Check(z.Rectify(), baseType: null);
    }

    /// <summary>Gets or sets the X component of the rotation axis.</summary>
    [IdlMember("x", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssrotate-x")]
    public CssNumberish X
    {
        get => _x;
        set => _x = Check(value.Rectify(), baseType: null);
    }

    /// <summary>Gets or sets the Y component of the rotation axis.</summary>
    [IdlMember("y", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssrotate-y")]
    public CssNumberish Y
    {
        get => _y;
        set => _y = Check(value.Rectify(), baseType: null);
    }

    /// <summary>Gets or sets the Z component of the rotation axis.</summary>
    [IdlMember("z", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssrotate-z")]
    public CssNumberish Z
    {
        get => _z;
        set => _z = Check(value.Rectify(), baseType: null);
    }

    /// <summary>Gets or sets the rotation angle.</summary>
    [IdlMember("angle", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssrotate-angle")]
    public CssNumericValue Angle
    {
        get => _angle;
        set => _angle = Check(value, CssNumericBaseType.Angle);
    }

    private protected override double[] GetMatrix()
    {
        var angle = ToUnit(_angle, "deg");
        return Is2D ? TransformMatrices.Rotation(0, 0, 1, angle) : TransformMatrices.Rotation(ToUnit(_x, "number"), ToUnit(_y, "number"), ToUnit(_z, "number"), angle);
    }

    /// <inheritdoc/>
    [NotInIdl("The stringifier of CSSTransformComponent")]
    public override string ToString() => Is2D ? SerializeFunction("rotate", _angle) : SerializeFunction("rotate3d", _x, _y, _z, _angle);
}

/// <summary>scale(), scaleX(), scaleY(), scaleZ() and scale3d() (https://drafts.css-houdini.org/css-typed-om-1/#cssscale).</summary>
[IdlInterface("CSSScale", "https://drafts.css-houdini.org/css-typed-om-1/#cssscale")]
public sealed class CssScale : CssTransformComponent
{
    private CssNumericValue _x;
    private CssNumericValue _y;
    private CssNumericValue _z;

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-cssscale-cssscale</summary>
    [IdlMember("constructor", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssscale-cssscale")]
    public CssScale(CssNumberish x, CssNumberish y, CssNumberish? z = null)
        : base(is2D: z is null)
    {
        _x = Check(x.Rectify(), baseType: null);
        _y = Check(y.Rectify(), baseType: null);
        _z = z is { } zValue ? Check(zValue.Rectify(), baseType: null) : new CssUnitValue(1, "number");
    }

    /// <summary>Gets or sets the scale factor along the X axis.</summary>
    [IdlMember("x", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssscale-x")]
    public CssNumberish X
    {
        get => _x;
        set => _x = Check(value.Rectify(), baseType: null);
    }

    /// <summary>Gets or sets the scale factor along the Y axis.</summary>
    [IdlMember("y", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssscale-y")]
    public CssNumberish Y
    {
        get => _y;
        set => _y = Check(value.Rectify(), baseType: null);
    }

    /// <summary>Gets or sets the scale factor along the Z axis.</summary>
    [IdlMember("z", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssscale-z")]
    public CssNumberish Z
    {
        get => _z;
        set => _z = Check(value.Rectify(), baseType: null);
    }

    private protected override double[] GetMatrix()
    {
        var x = ToUnit(_x, "number");
        var y = ToUnit(_y, "number");
        var z = Is2D ? 1 : ToUnit(_z, "number");
        return [x, 0, 0, 0, 0, y, 0, 0, 0, 0, z, 0, 0, 0, 0, 1];
    }

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#serialize-a-cssscale: scale(x) when x and y are equal.</summary>
    [NotInIdl("The stringifier of CSSTransformComponent")]
    public override string ToString()
    {
        if (!Is2D)
            return SerializeFunction("scale3d", _x, _y, _z);

        // https://drafts.css-houdini.org/css-typed-om-1/#serialize-a-cssscale omits a y scale equal to the x scale, but Blink
        // and WebKit always serialize both (css/css-transforms/animation/transform-interpolation-computed-value.html)
        return SerializeFunction("scale", _x, _y);
    }
}

/// <summary>skew() (https://drafts.css-houdini.org/css-typed-om-1/#cssskew).</summary>
[IdlInterface("CSSSkew", "https://drafts.css-houdini.org/css-typed-om-1/#cssskew")]
public sealed class CssSkew : CssTransformComponent
{
    private CssNumericValue _ax;
    private CssNumericValue _ay;

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-cssskew-cssskew</summary>
    [IdlMember("constructor", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssskew-cssskew")]
    public CssSkew(CssNumericValue ax, CssNumericValue ay)
        : base(is2D: true)
    {
        _ax = Check(ax, CssNumericBaseType.Angle);
        _ay = Check(ay, CssNumericBaseType.Angle);
    }

    /// <summary>Gets or sets the skew angle along the X axis.</summary>
    [IdlMember("ax", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssskew-ax")]
    public CssNumericValue Ax
    {
        get => _ax;
        set => _ax = Check(value, CssNumericBaseType.Angle);
    }

    /// <summary>Gets or sets the skew angle along the Y axis.</summary>
    [IdlMember("ay", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssskew-ay")]
    public CssNumericValue Ay
    {
        get => _ay;
        set => _ay = Check(value, CssNumericBaseType.Angle);
    }

    private protected override bool CanChangeIs2D => false;

    private protected override double[] GetMatrix() => TransformMatrices.Skew(ToUnit(_ax, "deg"), ToUnit(_ay, "deg"));

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#serialize-a-cssskew: skew(ax) when ay is zero.</summary>
    [NotInIdl("The stringifier of CSSTransformComponent")]
    public override string ToString() => _ay is CssUnitValue { Value: 0 } ? SerializeFunction("skew", _ax) : SerializeFunction("skew", _ax, _ay);
}

/// <summary>skewX() (https://drafts.css-houdini.org/css-typed-om-1/#cssskewx).</summary>
[IdlInterface("CSSSkewX", "https://drafts.css-houdini.org/css-typed-om-1/#cssskewx")]
public sealed class CssSkewX : CssTransformComponent
{
    private CssNumericValue _ax;

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-cssskewx-cssskewx</summary>
    [IdlMember("constructor", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssskewx-cssskewx")]
    public CssSkewX(CssNumericValue ax)
        : base(is2D: true)
    {
        _ax = Check(ax, CssNumericBaseType.Angle);
    }

    /// <summary>Gets or sets the skew angle along the X axis.</summary>
    [IdlMember("ax", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssskewx-ax")]
    public CssNumericValue Ax
    {
        get => _ax;
        set => _ax = Check(value, CssNumericBaseType.Angle);
    }

    private protected override bool CanChangeIs2D => false;

    private protected override double[] GetMatrix() => TransformMatrices.Skew(ToUnit(_ax, "deg"), 0);

    /// <inheritdoc/>
    [NotInIdl("The stringifier of CSSTransformComponent")]
    public override string ToString() => SerializeFunction("skewX", _ax);
}

/// <summary>skewY() (https://drafts.css-houdini.org/css-typed-om-1/#cssskewy).</summary>
[IdlInterface("CSSSkewY", "https://drafts.css-houdini.org/css-typed-om-1/#cssskewy")]
public sealed class CssSkewY : CssTransformComponent
{
    private CssNumericValue _ay;

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-cssskewy-cssskewy</summary>
    [IdlMember("constructor", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssskewy-cssskewy")]
    public CssSkewY(CssNumericValue ay)
        : base(is2D: true)
    {
        _ay = Check(ay, CssNumericBaseType.Angle);
    }

    /// <summary>Gets or sets the skew angle along the Y axis.</summary>
    [IdlMember("ay", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssskewy-ay")]
    public CssNumericValue Ay
    {
        get => _ay;
        set => _ay = Check(value, CssNumericBaseType.Angle);
    }

    private protected override bool CanChangeIs2D => false;

    private protected override double[] GetMatrix() => TransformMatrices.Skew(0, ToUnit(_ay, "deg"));

    /// <inheritdoc/>
    [NotInIdl("The stringifier of CSSTransformComponent")]
    public override string ToString() => SerializeFunction("skewY", _ay);
}

/// <summary>The CSSPerspectiveValue typedef: (CSSNumericValue or CSSKeywordish) (https://drafts.css-houdini.org/css-typed-om-1/#typedefdef-cssperspectivevalue).</summary>
public readonly struct CssPerspectiveValue : IUnionValue, IEquatable<CssPerspectiveValue>
{
    private CssPerspectiveValue(object value) => Value = value;

    /// <summary>Gets the value: a <see cref="CssNumericValue"/>, a <see cref="string"/> or a <see cref="CssKeywordValue"/>.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Converts a <c>CssNumericValue</c> to a <c>CssPerspectiveValue</c>.</summary>
    [NotInIdl]
    public static implicit operator CssPerspectiveValue(CssNumericValue value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts a <c>string</c> to a <c>CssPerspectiveValue</c>.</summary>
    [NotInIdl]
    public static implicit operator CssPerspectiveValue(string value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts a <c>CssKeywordValue</c> to a <c>CssPerspectiveValue</c>.</summary>
    [NotInIdl]
    public static implicit operator CssPerspectiveValue(CssKeywordValue value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates a <c>CssPerspectiveValue</c> from a <c>CssNumericValue</c>.</summary>
    [NotInIdl]
    public static CssPerspectiveValue FromCssNumericValue(CssNumericValue value) => value;

    /// <summary>Creates a <c>CssPerspectiveValue</c> from a <c>string</c>.</summary>
    [NotInIdl]
    public static CssPerspectiveValue FromString(string value) => value;

    /// <summary>Creates a <c>CssPerspectiveValue</c> from a <c>CssKeywordValue</c>.</summary>
    [NotInIdl]
    public static CssPerspectiveValue FromCssKeywordValue(CssKeywordValue value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(CssPerspectiveValue other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is CssPerspectiveValue other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <summary>Determines whether two <c>CssPerspectiveValue</c> values are equal.</summary>
    [NotInIdl]
    public static bool operator ==(CssPerspectiveValue left, CssPerspectiveValue right) => left.Equals(right);

    /// <summary>Determines whether two <c>CssPerspectiveValue</c> values are not equal.</summary>
    [NotInIdl]
    public static bool operator !=(CssPerspectiveValue left, CssPerspectiveValue right) => !left.Equals(right);
}

/// <summary>perspective() (https://drafts.css-houdini.org/css-typed-om-1/#cssperspective).</summary>
[IdlInterface("CSSPerspective", "https://drafts.css-houdini.org/css-typed-om-1/#cssperspective")]
public sealed class CssPerspective : CssTransformComponent
{
    private CssStyleValue _length;

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-cssperspective-cssperspective</summary>
    [IdlMember("constructor", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssperspective-cssperspective")]
    public CssPerspective(CssPerspectiveValue length)
        : base(is2D: false)
    {
        _length = Validate(length);
    }

    /// <summary>The length: a <see cref="CssNumericValue"/> or the keyword none (a <see cref="CssKeywordValue"/>).</summary>
    [IdlMember("length", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssperspective-length")]
    public CssPerspectiveValue Length
    {
        get => _length is CssNumericValue numeric ? numeric : (CssKeywordValue)_length;
        set => _length = Validate(value);
    }

    private protected override bool CanChangeIs2D => false;

    private static CssStyleValue Validate(CssPerspectiveValue length)
    {
        switch (length.Value)
        {
            case CssNumericValue numeric:
                return Check(numeric, CssNumericBaseType.Length);

            case var keywordish:
            {
                var keyword = keywordish as CssKeywordValue ?? new CssKeywordValue(keywordish as string ?? "");
                if (!InfraAscii.EqualsIgnoreAsciiCase(keyword.Value, "none"))
                    throw new TypeErrorException("The perspective must be a length or the keyword none");

                return keyword;
            }
        }
    }

    private protected override double[] GetMatrix()
    {
        if (_length is not CssNumericValue numeric)
            return TransformMatrices.Identity();

        // https://drafts.csswg.org/css-transforms-2/#funcdef-perspective: a depth less than 1px is treated as 1px
        var depth = Math.Max(1, ToUnit(numeric, "px"));
        return [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, -1 / depth, 0, 0, 0, 1];
    }

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#serialize-a-cssperspective: a negative length is wrapped in calc().</summary>
    [NotInIdl("The stringifier of CSSTransformComponent")]
    public override string ToString()
    {
        var length = _length switch
        {
            CssUnitValue unit => unit.Serialize(minimum: new CssUnitValue(0, "px")),
            _ => _length.ToString(),
        };

        return "perspective(" + length + ")";
    }
}

/// <summary>The CSSMatrixComponentOptions dictionary (https://drafts.css-houdini.org/css-typed-om-1/#dictdef-cssmatrixcomponentoptions).</summary>
[IdlInterface("CSSMatrixComponentOptions", "https://drafts.css-houdini.org/css-typed-om-1/#dictdef-cssmatrixcomponentoptions")]
public sealed class CssMatrixComponentOptions
{
    /// <summary>
    /// Gets a value indicating whether the matrix component is 2D (<c>matrix()</c>) or 3D (<c>matrix3d()</c>).
    /// </summary>
    [IdlMember("is2D", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssmatrixcomponentoptions-is2d")]
    public bool? Is2D { get; init; }
}

/// <summary>matrix() and matrix3d() (https://drafts.css-houdini.org/css-typed-om-1/#cssmatrixcomponent).</summary>
[IdlInterface("CSSMatrixComponent", "https://drafts.css-houdini.org/css-typed-om-1/#cssmatrixcomponent")]
public sealed class CssMatrixComponent : CssTransformComponent
{
    private DomMatrix _matrix;

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-cssmatrixcomponent-cssmatrixcomponent</summary>
    [IdlMember("constructor", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssmatrixcomponent-cssmatrixcomponent")]
    public CssMatrixComponent(DomMatrixReadOnly matrix, CssMatrixComponentOptions? options = null)
        : base(is2D: options?.Is2D ?? GetIs2D(matrix))
    {
        // The attribute is a DOMMatrix: a DOMMatrixReadOnly is copied
        _matrix = matrix as DomMatrix ?? matrix.CloneMutable();
    }

    /// <summary>Gets or sets the matrix of the component.</summary>
    [IdlMember("matrix", "https://drafts.css-houdini.org/css-typed-om-1/#dom-cssmatrixcomponent-matrix")]
    public DomMatrix Matrix
    {
        get => _matrix;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _matrix = value;
        }
    }

    private static bool GetIs2D(DomMatrixReadOnly matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        return matrix.Is2D;
    }

    private protected override double[] GetMatrix()
    {
        var elements = _matrix.Elements.ToArray();
        if (!Is2D)
            return elements;

        // The 2D flattening keeps a, b, c, d, e and f (https://drafts.fxtf.org/geometry/#matrix-is-2d)
        return [elements[0], elements[1], 0, 0, elements[4], elements[5], 0, 0, 0, 0, 1, 0, elements[12], elements[13], 0, 1];
    }

    /// <summary>The serialization of the matrix (https://drafts.css-houdini.org/css-typed-om-1/#serialize-a-cssmatrixcomponent).</summary>
    [NotInIdl("The stringifier of CSSTransformComponent")]
    public override string ToString() => _matrix.ToString();
}

/// <summary>The matrices of the transform functions (https://drafts.csswg.org/css-transforms-2/#mathematical-description), in column-major order.</summary>
internal static class TransformMatrices
{
    public static double[] Identity() => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

    /// <summary>https://drafts.csswg.org/css-transforms-2/#Rotate3dDefined</summary>
    public static double[] Rotation(double x, double y, double z, double angleDegrees)
    {
        var length = Math.Sqrt((x * x) + (y * y) + (z * z));
        if (length == 0)
            return Identity();

        x /= length;
        y /= length;
        z /= length;
        var alpha = angleDegrees * Math.PI / 180;
        var sc = Math.Sin(alpha / 2) * Math.Cos(alpha / 2);
        var sq = Math.Sin(alpha / 2) * Math.Sin(alpha / 2);
        return
        [
            1 - (2 * ((y * y) + (z * z)) * sq), 2 * ((x * y * sq) + (z * sc)), 2 * ((x * z * sq) - (y * sc)), 0,
            2 * ((x * y * sq) - (z * sc)), 1 - (2 * ((x * x) + (z * z)) * sq), 2 * ((y * z * sq) + (x * sc)), 0,
            2 * ((x * z * sq) + (y * sc)), 2 * ((y * z * sq) - (x * sc)), 1 - (2 * ((x * x) + (y * y)) * sq), 0,
            0, 0, 0, 1,
        ];
    }

    /// <summary>https://drafts.csswg.org/css-transforms-1/#SkewDefined</summary>
    public static double[] Skew(double axDegrees, double ayDegrees) =>
        [1, Math.Tan(ayDegrees * Math.PI / 180), 0, 0, Math.Tan(axDegrees * Math.PI / 180), 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

    /// <summary>Returns the product a × b of two matrices.</summary>
    public static double[] Multiply(ReadOnlySpan<double> a, ReadOnlySpan<double> b)
    {
        var result = new double[16];
        for (var column = 0; column < 4; column++)
        {
            for (var row = 0; row < 4; row++)
            {
                double sum = 0;
                for (var k = 0; k < 4; k++)
                {
                    sum += a[(k * 4) + row] * b[(column * 4) + k];
                }

                result[(column * 4) + row] = sum;
            }
        }

        return result;
    }
}
