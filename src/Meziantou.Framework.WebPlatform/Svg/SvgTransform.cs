using System.Text;
using Meziantou.Framework.WebPlatform.Svg;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://svgwg.org/svg2-draft/coords.html#InterfaceSVGTransform</summary>
[IdlInterface("SVGTransform", "https://w3c.github.io/svgwg/svg2-draft/coords.html#InterfaceSVGTransform")]
public sealed class SvgTransform
{
    /// <summary>The type of a transform that is not one of the other types.</summary>
    [IdlMember("SVG_TRANSFORM_UNKNOWN", "https://w3c.github.io/svgwg/svg2-draft/coords.html#__svg__SVGTransform__SVG_TRANSFORM_UNKNOWN")]
    public const ushort SvgTransformUnknown = 0;

    /// <summary>The type of a transform defined by a matrix.</summary>
    [IdlMember("SVG_TRANSFORM_MATRIX", "https://w3c.github.io/svgwg/svg2-draft/coords.html#__svg__SVGTransform__SVG_TRANSFORM_MATRIX")]
    public const ushort SvgTransformMatrix = 1;

    /// <summary>The type of a translation.</summary>
    [IdlMember("SVG_TRANSFORM_TRANSLATE", "https://w3c.github.io/svgwg/svg2-draft/coords.html#__svg__SVGTransform__SVG_TRANSFORM_TRANSLATE")]
    public const ushort SvgTransformTranslate = 2;

    /// <summary>The type of a scale.</summary>
    [IdlMember("SVG_TRANSFORM_SCALE", "https://w3c.github.io/svgwg/svg2-draft/coords.html#__svg__SVGTransform__SVG_TRANSFORM_SCALE")]
    public const ushort SvgTransformScale = 3;

    /// <summary>The type of a rotation.</summary>
    [IdlMember("SVG_TRANSFORM_ROTATE", "https://w3c.github.io/svgwg/svg2-draft/coords.html#__svg__SVGTransform__SVG_TRANSFORM_ROTATE")]
    public const ushort SvgTransformRotate = 4;

    /// <summary>The type of a skew along the x axis.</summary>
    [IdlMember("SVG_TRANSFORM_SKEWX", "https://w3c.github.io/svgwg/svg2-draft/coords.html#__svg__SVGTransform__SVG_TRANSFORM_SKEWX")]
    public const ushort SvgTransformSkewX = 5;

    /// <summary>The type of a skew along the y axis.</summary>
    [IdlMember("SVG_TRANSFORM_SKEWY", "https://w3c.github.io/svgwg/svg2-draft/coords.html#__svg__SVGTransform__SVG_TRANSFORM_SKEWY")]
    public const ushort SvgTransformSkewY = 6;

    private readonly TransformMatrix _matrix;
    private ushort _type = SvgTransformMatrix;
    private float _angle;
    private float _rotateCenterX;
    private float _rotateCenterY;

    internal SvgTransform(ISvgValueOwner? owner = null)
    {
        Owner = owner;
        _matrix = new TransformMatrix(this);
    }

    internal ISvgValueOwner? Owner { get; set; }

    /// <summary>Gets the type of the transform, one of the <c>SVG_TRANSFORM_*</c> constants.</summary>
    [IdlMember("type", "https://w3c.github.io/svgwg/svg2-draft/coords.html#__svg__SVGTransform__type")]
    public ushort Type
    {
        get
        {
            Owner?.Sync();
            return _type;
        }
    }

    /// <summary>The matrix of the transform (https://svgwg.org/svg2-draft/coords.html#__svg__SVGTransform__matrix); modifying it changes the type to SVG_TRANSFORM_MATRIX.</summary>
    [IdlMember("matrix", "https://w3c.github.io/svgwg/svg2-draft/coords.html#__svg__SVGTransform__matrix")]
    public DomMatrix Matrix
    {
        get
        {
            Owner?.Sync();
            return _matrix;
        }
    }

    /// <summary>Gets the angle, in degrees, of a rotation or skew transform, and 0 for the other types.</summary>
    [IdlMember("angle", "https://w3c.github.io/svgwg/svg2-draft/coords.html#__svg__SVGTransform__angle")]
    public float Angle
    {
        get
        {
            Owner?.Sync();
            return _angle;
        }
    }

    /// <summary>Sets the transform to the given matrix and its type to <c>SVG_TRANSFORM_MATRIX</c>.</summary>
    [IdlMember("setMatrix", "https://w3c.github.io/svgwg/svg2-draft/coords.html#__svg__SVGTransform__setMatrix")]
    public void SetMatrix(DomMatrix2DInit? matrix = null)
    {
        var value = DomMatrixReadOnly.FromMatrixInit(matrix);
        Change(() =>
        {
            _type = SvgTransformMatrix;
            _angle = 0;
            _matrix.SetRaw(value.A, value.B, value.C, value.D, value.E, value.F);
        });
    }

    /// <summary>Sets the transform to a translation by the given offsets.</summary>
    [IdlMember("setTranslate", "https://w3c.github.io/svgwg/svg2-draft/coords.html#__svg__SVGTransform__setTranslate")]
    public void SetTranslate(float tx, float ty) => Change(() => SetTranslateRaw(tx, ty));

    /// <summary>Sets the transform to a scale by the given factors.</summary>
    [IdlMember("setScale", "https://w3c.github.io/svgwg/svg2-draft/coords.html#__svg__SVGTransform__setScale")]
    public void SetScale(float sx, float sy) => Change(() => SetScaleRaw(sx, sy));

    /// <summary>Sets the transform to a rotation by the given angle, in degrees, around the given center.</summary>
    [IdlMember("setRotate", "https://w3c.github.io/svgwg/svg2-draft/coords.html#__svg__SVGTransform__setRotate")]
    public void SetRotate(float angle, float cx, float cy) => Change(() => SetRotateRaw(angle, cx, cy));

    /// <summary>Sets the transform to a skew along the x axis by the given angle, in degrees.</summary>
    [IdlMember("setSkewX", "https://w3c.github.io/svgwg/svg2-draft/coords.html#__svg__SVGTransform__setSkewX")]
    public void SetSkewX(float angle) => Change(() => SetSkewRaw(SvgTransformSkewX, angle));

    /// <summary>Sets the transform to a skew along the y axis by the given angle, in degrees.</summary>
    [IdlMember("setSkewY", "https://w3c.github.io/svgwg/svg2-draft/coords.html#__svg__SVGTransform__setSkewY")]
    public void SetSkewY(float angle) => Change(() => SetSkewRaw(SvgTransformSkewY, angle));

    private void Change(Action action)
    {
        Owner.BeginChange();
        action();
        Owner?.Commit();
    }

    internal void SetTranslateRaw(float tx, float ty)
    {
        _type = SvgTransformTranslate;
        _angle = 0;
        _matrix.SetRaw(1, 0, 0, 1, tx, ty);
    }

    internal void SetScaleRaw(float sx, float sy)
    {
        _type = SvgTransformScale;
        _angle = 0;
        _matrix.SetRaw(sx, 0, 0, sy, 0, 0);
    }

    internal void SetRotateRaw(float angle, float cx, float cy)
    {
        _type = SvgTransformRotate;
        _angle = angle;
        _rotateCenterX = cx;
        _rotateCenterY = cy;
        var radians = angle * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        // translate(cx, cy) rotate(angle) translate(-cx, -cy)
        _matrix.SetRaw(cos, sin, -sin, cos, cx - (cos * cx) + (sin * cy), cy - (sin * cx) - (cos * cy));
    }

    internal void SetSkewRaw(ushort type, float angle)
    {
        _type = type;
        _angle = angle;
        var tan = Math.Tan(angle * Math.PI / 180);
        if (type == SvgTransformSkewX)
        {
            _matrix.SetRaw(1, 0, tan, 1, 0, 0);
        }
        else
        {
            _matrix.SetRaw(1, tan, 0, 1, 0, 0);
        }
    }

    internal void SetMatrixRaw(double a, double b, double c, double d, double e, double f)
    {
        _type = SvgTransformMatrix;
        _angle = 0;
        _matrix.SetRaw(a, b, c, d, e, f);
    }

    internal void AssignFrom(SvgTransform source)
    {
        _type = source._type;
        _angle = source._angle;
        _rotateCenterX = source._rotateCenterX;
        _rotateCenterY = source._rotateCenterY;
        _matrix.SetRaw(source._matrix.A, source._matrix.B, source._matrix.C, source._matrix.D, source._matrix.E, source._matrix.F);
    }

    internal SvgTransform Copy()
    {
        var copy = new SvgTransform
        {
            _type = _type,
            _angle = _angle,
            _rotateCenterX = _rotateCenterX,
            _rotateCenterY = _rotateCenterY,
        };
        copy._matrix.SetRaw(_matrix.A, _matrix.B, _matrix.C, _matrix.D, _matrix.E, _matrix.F);
        return copy;
    }

    internal string Serialize()
    {
        static string N(double value) => SvgParsing.FormatNumber((float)value);
        return _type switch
        {
            SvgTransformTranslate => $"translate({N(_matrix.E)} {N(_matrix.F)})",
            SvgTransformScale => $"scale({N(_matrix.A)} {N(_matrix.D)})",
            SvgTransformRotate when _rotateCenterX == 0 && _rotateCenterY == 0 => $"rotate({N(_angle)})",
            SvgTransformRotate => $"rotate({N(_angle)} {N(_rotateCenterX)} {N(_rotateCenterY)})",
            SvgTransformSkewX => $"skewX({N(_angle)})",
            SvgTransformSkewY => $"skewY({N(_angle)})",
            _ => $"matrix({N(_matrix.A)} {N(_matrix.B)} {N(_matrix.C)} {N(_matrix.D)} {N(_matrix.E)} {N(_matrix.F)})",
        };
    }

    /// <summary>
    /// Converts a transform list of the SVG syntax to a CSS &lt;transform-list&gt; (the value of the presentation attribute of the
    /// transform property, https://drafts.csswg.org/css-transforms-1/#svg-transform): the lengths are user units (px), the angles
    /// degrees, and a rotation around a center is a rotation between two translations.
    /// </summary>
    internal static string ToCss(List<SvgTransform> transforms)
    {
        var builder = new StringBuilder();
        foreach (var transform in transforms)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            var m = transform._matrix;
            switch (transform._type)
            {
                case SvgTransformTranslate:
                    builder.Append("translate(").Append(N(m.E)).Append("px, ").Append(N(m.F)).Append("px)");
                    break;
                case SvgTransformScale:
                    builder.Append("scale(").Append(N(m.A)).Append(", ").Append(N(m.D)).Append(')');
                    break;
                case SvgTransformRotate when transform._rotateCenterX == 0 && transform._rotateCenterY == 0:
                    builder.Append("rotate(").Append(N(transform._angle)).Append("deg)");
                    break;
                case SvgTransformRotate:
                    builder.Append("translate(").Append(N(transform._rotateCenterX)).Append("px, ").Append(N(transform._rotateCenterY)).Append("px) rotate(")
                        .Append(N(transform._angle)).Append("deg) translate(").Append(N(-transform._rotateCenterX)).Append("px, ").Append(N(-transform._rotateCenterY)).Append("px)");
                    break;
                case SvgTransformSkewX:
                    builder.Append("skewX(").Append(N(transform._angle)).Append("deg)");
                    break;
                case SvgTransformSkewY:
                    builder.Append("skewY(").Append(N(transform._angle)).Append("deg)");
                    break;
                default:
                    builder.Append("matrix(").Append(N(m.A)).Append(", ").Append(N(m.B)).Append(", ").Append(N(m.C)).Append(", ").Append(N(m.D)).Append(", ").Append(N(m.E)).Append(", ").Append(N(m.F)).Append(')');
                    break;
            }
        }

        return builder.ToString();

        static string N(double value) => Css.Values.CssSerializer.SerializeNumber(value);
    }

    internal static string Serialize(IEnumerable<SvgTransform> transforms)
    {
        var builder = new StringBuilder();
        foreach (var transform in transforms)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(transform.Serialize());
        }

        return builder.ToString();
    }

    /// <summary>Parses a transform list (https://svgwg.org/svg2-draft/coords.html#TransformProperty).</summary>
    internal static bool TryParseList(string? value, out List<SvgTransform> result)
    {
        result = [];
        if (value is null)
            return false;

        var position = 0;
        SvgParsing.SkipWhitespace(value, ref position);
        while (position < value.Length)
        {
            var nameStart = position;
            while (position < value.Length && char.IsAsciiLetter(value[position]))
            {
                position++;
            }

            var name = value[nameStart..position];
            SvgParsing.SkipWhitespace(value, ref position);
            if (position >= value.Length || value[position] != '(')
                return false;

            position++;
            SvgParsing.SkipWhitespace(value, ref position);
            var arguments = new List<float>();
            while (position < value.Length && value[position] != ')')
            {
                if (!SvgParsing.TryParseNumber(value, ref position, out var number) || !float.IsFinite((float)number))
                    return false;

                arguments.Add((float)number);
                var comma = SvgParsing.SkipCommaWhitespace(value, ref position);
                if (comma && position < value.Length && value[position] == ')')
                    return false;
            }

            if (position >= value.Length)
                return false;

            position++;
            var transform = new SvgTransform();
            switch (name, arguments.Count)
            {
                case ("matrix", 6):
                    transform.SetMatrixRaw(arguments[0], arguments[1], arguments[2], arguments[3], arguments[4], arguments[5]);
                    break;
                case ("translate", 1 or 2):
                    transform.SetTranslateRaw(arguments[0], arguments.Count == 2 ? arguments[1] : 0);
                    break;
                case ("scale", 1 or 2):
                    transform.SetScaleRaw(arguments[0], arguments.Count == 2 ? arguments[1] : arguments[0]);
                    break;
                case ("rotate", 1):
                    transform.SetRotateRaw(arguments[0], 0, 0);
                    break;
                case ("rotate", 3):
                    transform.SetRotateRaw(arguments[0], arguments[1], arguments[2]);
                    break;
                case ("skewX", 1):
                    transform.SetSkewRaw(SvgTransformSkewX, arguments[0]);
                    break;
                case ("skewY", 1):
                    transform.SetSkewRaw(SvgTransformSkewY, arguments[0]);
                    break;
                default:
                    return false;
            }

            result.Add(transform);
            SvgParsing.SkipCommaWhitespace(value, ref position);
        }

        return true;
    }

    /// <summary>The matrix of a transform: a change makes the transform a matrix transform.</summary>
    private sealed class TransformMatrix(SvgTransform transform) : DomMatrix
    {
        private bool _updating;

        public void SetRaw(double a, double b, double c, double d, double e, double f)
        {
            _updating = true;
            try
            {
                SetComponents(a, b, c, d, e, f);
            }
            finally
            {
                _updating = false;
            }
        }

        private protected override void OnChanging()
        {
            if (!_updating)
            {
                transform.Owner.BeginChange();
            }
        }

        private protected override void OnChanged()
        {
            if (_updating)
                return;

            transform._type = SvgTransformMatrix;
            transform._angle = 0;
            transform.Owner?.Commit();
        }
    }
}
