using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Svg;

/// <summary>A segment of path data: a command letter and its values (https://svgwg.org/svg2-draft/paths.html#PathData).</summary>
/// <remarks>The values are never mutated: they are copied when an <see cref="SvgPathSegment"/> is created.</remarks>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct SvgPathCommand(char Type, float[] Values)
{
    public SvgPathSegment ToSegment() => new(Type.ToString(), (float[])Values.Clone());
}

/// <summary>Parsing, normalization and serialization of path data (https://svgwg.org/svg2-draft/paths.html#PathDataBNF).</summary>
internal static class SvgPathData
{
    /// <summary>
    /// Parses the value of a d attribute. The path data is processed up to the first error: the result contains the segments
    /// before the segment in error (https://svgwg.org/svg2-draft/implnote.html#PathElementImplementationNotes). An implicit
    /// command following a moveto is a lineto.
    /// </summary>
    public static List<SvgPathCommand> Parse(ReadOnlySpan<char> value) => Parse(value, out _);

    /// <summary>Parses path data; <paramref name="complete"/> is false when the path data contains an error.</summary>
    public static List<SvgPathCommand> Parse(ReadOnlySpan<char> value, out bool complete)
    {
        var result = new List<SvgPathCommand>();
        var position = 0;
        SkipWhitespace(value, ref position);
        var previous = '\0';
        while (position < value.Length)
        {
            var c = value[position];
            char command;
            if (previous == '\0')
            {
                // The path data must begin with a moveto
                if (c is not ('M' or 'm'))
                    break;

                command = c;
                position++;
            }
            else if (GetValueCount(c) >= 0)
            {
                command = c;
                position++;
            }
            else if (IsNumberStart(c) && previous is not ('Z' or 'z'))
            {
                // An implicit command: the same command, or a lineto after a moveto
                command = previous switch
                {
                    'M' => 'L',
                    'm' => 'l',
                    _ => previous,
                };
            }
            else
            {
                break;
            }

            previous = command;
            if (!TryParseValues(value, ref position, command, out var values))
            {
                // The values of the last segment may be truncated at the end of the path data
                complete = false;
                return result;
            }

            result.Add(new SvgPathCommand(command, values));
        }

        complete = position >= value.Length;
        return result;
    }

    /// <summary>The number of values of a command, or -1 if the character is not a command.</summary>
    public static int GetValueCount(char command) => command switch
    {
        'Z' or 'z' => 0,
        'H' or 'h' or 'V' or 'v' => 1,
        'M' or 'm' or 'L' or 'l' or 'T' or 't' => 2,
        'S' or 's' or 'Q' or 'q' => 4,
        'C' or 'c' => 6,
        'A' or 'a' => 7,
        _ => -1,
    };

    private static bool IsNumberStart(char c) => char.IsAsciiDigit(c) || c is '+' or '-' or '.';

    private static bool TryParseValues(ReadOnlySpan<char> value, ref int position, char command, out float[] values)
    {
        var count = GetValueCount(command);
        values = count == 0 ? [] : new float[count];
        if (count == 0)
        {
            SkipWhitespace(value, ref position);
            return true;
        }

        SkipWhitespace(value, ref position);
        for (var i = 0; i < count; i++)
        {
            if (command is 'A' or 'a' && i is 3 or 4)
            {
                // The flags are a single character, that may not be followed by a separator (e.g. "a1 1 0 00 1 1")
                if (position >= value.Length || value[position] is not ('0' or '1'))
                    return false;

                values[i] = value[position] - '0';
                position++;
            }
            else if (!TryParseNumber(value, ref position, out values[i]))
            {
                return false;
            }

            SkipCommaWhitespace(value, ref position);
        }

        return true;
    }

    /// <summary>
    /// Parses a number (https://svgwg.org/svg2-draft/paths.html#PathDataBNF). A decimal point must be followed by a digit and
    /// an exponent must have digits ("1." and "1e" are errors); the value must be a finite float.
    /// </summary>
    private static bool TryParseNumber(ReadOnlySpan<char> value, ref int position, out float result)
    {
        result = 0;
        var i = position;
        if (i < value.Length && value[i] is '+' or '-')
        {
            i++;
        }

        var integerStart = i;
        while (i < value.Length && char.IsAsciiDigit(value[i]))
        {
            i++;
        }

        var hasDigits = i > integerStart;
        if (i < value.Length && value[i] == '.')
        {
            i++;
            if (i >= value.Length || !char.IsAsciiDigit(value[i]))
                return false;

            while (i < value.Length && char.IsAsciiDigit(value[i]))
            {
                i++;
            }

            hasDigits = true;
        }

        if (!hasDigits)
            return false;

        if (i < value.Length && value[i] is 'e' or 'E')
        {
            i++;
            if (i < value.Length && value[i] is '+' or '-')
            {
                i++;
            }

            if (i >= value.Length || !char.IsAsciiDigit(value[i]))
                return false;

            while (i < value.Length && char.IsAsciiDigit(value[i]))
            {
                i++;
            }
        }

        if (!double.TryParse(value[position..i], NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !float.IsFinite((float)number))
            return false;

        result = (float)number;
        position = i;
        return true;
    }

    private static void SkipWhitespace(ReadOnlySpan<char> value, ref int position)
    {
        while (position < value.Length && InfraAscii.IsAsciiWhitespace(value[position]))
        {
            position++;
        }
    }

    private static void SkipCommaWhitespace(ReadOnlySpan<char> value, ref int position)
    {
        SkipWhitespace(value, ref position);
        if (position < value.Length && value[position] == ',')
        {
            position++;
            SkipWhitespace(value, ref position);
        }
    }

    /// <summary>
    /// Validates the segments given to setPathData(): the valid prefix of the segments. A segment is invalid when its type is not
    /// a command letter, when it does not have the number of values of the command or when a value is not finite; the first
    /// segment must be a moveto. The flags of an arc are true when they are not zero.
    /// </summary>
    public static List<SvgPathCommand> FromSegments(IEnumerable<SvgPathSegment> segments)
    {
        var result = new List<SvgPathCommand>();
        foreach (var segment in segments)
        {
            if (segment is null)
                throw new ArgumentException("The path data contains a null segment", nameof(segments));

            if (segment.Type is not [var command])
                break;

            var count = GetValueCount(command);
            if (count < 0 || (result.Count == 0 && command is not ('M' or 'm')))
                break;

            var values = segment.Values;
            if (values is null || values.Count != count)
                break;

            var copy = new float[count];
            var valid = true;
            for (var i = 0; i < count; i++)
            {
                var number = values[i];
                if (!float.IsFinite(number))
                {
                    valid = false;
                    break;
                }

                copy[i] = command is 'A' or 'a' && i is 3 or 4 ? (number != 0 ? 1 : 0) : number;
            }

            if (!valid)
                break;

            result.Add(new SvgPathCommand(command, copy));
        }

        return result;
    }

    /// <summary>Serializes path data: each command letter followed by its values, separated by a space. A closepath is serialized as "Z".</summary>
    public static string Serialize(List<SvgPathCommand> commands)
    {
        var builder = new StringBuilder();
        foreach (var command in commands)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(command.Type is 'z' ? 'Z' : command.Type);
            foreach (var value in command.Values)
            {
                builder.Append(' ').Append(SvgParsing.FormatNumber(value));
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Converts the path data to absolute commands, keeping the type of the commands (e.g. "m 10 20 h 30" becomes "M 10 20 H 40"):
    /// the computed value of the d property (https://svgwg.org/svg2-draft/paths.html#TheDProperty).
    /// </summary>
    public static List<SvgPathCommand> ToAbsolute(List<SvgPathCommand> commands)
    {
        var result = new List<SvgPathCommand>(commands.Count);
        float x = 0, y = 0, subpathStartX = 0, subpathStartY = 0;
        foreach (var command in commands)
        {
            var type = char.ToUpperInvariant(command.Type);
            var values = command.Values;
            if (type != command.Type && values.Length > 0)
            {
                values = (float[])values.Clone();
                switch (type)
                {
                    case 'H':
                        values[0] += x;
                        break;
                    case 'V':
                        values[0] += y;
                        break;
                    case 'A':
                        values[5] += x;
                        values[6] += y;
                        break;
                    default:
                        for (var i = 0; i < values.Length; i += 2)
                        {
                            values[i] += x;
                            values[i + 1] += y;
                        }

                        break;
                }
            }

            switch (type)
            {
                case 'Z':
                    x = subpathStartX;
                    y = subpathStartY;
                    break;
                case 'H':
                    x = values[0];
                    break;
                case 'V':
                    y = values[0];
                    break;
                default:
                    x = values[^2];
                    y = values[^1];
                    if (type == 'M')
                    {
                        subpathStartX = x;
                        subpathStartY = y;
                    }

                    break;
            }

            result.Add(new SvgPathCommand(type, values));
        }

        return result;
    }

    /// <summary>
    /// Converts the path data to the absolute moveto, lineto, cubic Bézier and closepath commands (M, L, C and Z): the horizontal
    /// and vertical linetos become linetos, the smooth and quadratic Béziers and the arcs become cubic Béziers.
    /// </summary>
    public static List<SvgPathCommand> Normalize(List<SvgPathCommand> commands)
    {
        var result = new List<SvgPathCommand>(commands.Count);
        var normalizer = new Normalizer();
        foreach (var command in commands)
        {
            normalizer.Emit(command, static (result, segment) => result.Add(segment.ToCommand()), result);
        }

        return result;
    }

    /// <summary>A normalized segment (M, L, C or Z, or A when the normalizer keeps the arcs) in absolute coordinates.</summary>
    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct NormalizedSegment(char Type, SvgPoint Point1, SvgPoint Point2, SvgPoint Target, SvgArc Arc = default)
    {
        public SvgPathCommand ToCommand() => Type switch
        {
            'M' or 'L' => new SvgPathCommand(Type, [(float)Target.X, (float)Target.Y]),
            'C' => new SvgPathCommand(Type, [(float)Point1.X, (float)Point1.Y, (float)Point2.X, (float)Point2.Y, (float)Target.X, (float)Target.Y]),
            _ => new SvgPathCommand('Z', []),
        };
    }

    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct SvgPoint(double X, double Y)
    {
        public static SvgPoint operator +(SvgPoint a, SvgPoint b) => new(a.X + b.X, a.Y + b.Y);

        public static SvgPoint operator -(SvgPoint a, SvgPoint b) => new(a.X - b.X, a.Y - b.Y);

        public static SvgPoint operator *(SvgPoint a, double factor) => new(a.X * factor, a.Y * factor);

        public double Length => Math.Sqrt((X * X) + (Y * Y));

        public static SvgPoint Lerp(SvgPoint a, SvgPoint b, double t) => new(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t));
    }

    /// <summary>
    /// An elliptical arc in center parameterization (https://svgwg.org/svg2-draft/implnote.html#ArcConversionEndpointToCenter):
    /// the points of the arc are Center + rotate(Cos, Sin) · (RadiusX cos θ, RadiusY sin θ), for θ from StartAngle to StartAngle + SweepAngle.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct SvgArc(SvgPoint Center, double RadiusX, double RadiusY, double Cos, double Sin, double StartAngle, double SweepAngle)
    {
        public SvgPoint PointAt(double theta)
        {
            var x = RadiusX * Math.Cos(theta);
            var y = RadiusY * Math.Sin(theta);
            return new SvgPoint(Center.X + (Cos * x) - (Sin * y), Center.Y + (Sin * x) + (Cos * y));
        }

        /// <summary>The derivative of the point with respect to the angle.</summary>
        public SvgPoint DerivativeAt(double theta)
        {
            var x = -RadiusX * Math.Sin(theta);
            var y = RadiusY * Math.Cos(theta);
            return new SvgPoint((Cos * x) - (Sin * y), (Sin * x) + (Cos * y));
        }

        /// <summary>The norm of the derivative of the point with respect to the angle.</summary>
        public double SpeedAt(double theta)
        {
            var x = RadiusX * Math.Sin(theta);
            var y = RadiusY * Math.Cos(theta);
            return Math.Sqrt((x * x) + (y * y));
        }

        /// <summary>
        /// Computes the center parameterization of an arc (https://svgwg.org/svg2-draft/implnote.html#ArcConversionEndpointToCenter).
        /// The radii are scaled up when they are too small (https://svgwg.org/svg2-draft/implnote.html#ArcCorrectionOutOfRangeRadii).
        /// Returns false when the arc is a straight line: a radius is zero, or the endpoints are identical.
        /// </summary>
        public static bool TryCreate(SvgPoint start, double rx, double ry, double angleDegrees, bool largeArc, bool sweep, SvgPoint end, out SvgArc arc)
        {
            arc = default;
            rx = Math.Abs(rx);
            ry = Math.Abs(ry);
            if (rx == 0 || ry == 0 || start == end)
                return false;

            var angle = angleDegrees * Math.PI / 180;
            var cos = Math.Cos(angle);
            var sin = Math.Sin(angle);

            // The midpoint in the coordinate system of the ellipse
            var dx = (start.X - end.X) / 2;
            var dy = (start.Y - end.Y) / 2;
            var x1 = (cos * dx) + (sin * dy);
            var y1 = (-sin * dx) + (cos * dy);
            var lambda = (x1 * x1 / (rx * rx)) + (y1 * y1 / (ry * ry));
            if (lambda > 1)
            {
                var scale = Math.Sqrt(lambda);
                rx *= scale;
                ry *= scale;
            }

            // The center, in the space where the ellipse is the unit circle
            SvgPoint ToUnit(SvgPoint p) => new(((cos * p.X) + (sin * p.Y)) / rx, ((-sin * p.X) + (cos * p.Y)) / ry);
            var point1 = ToUnit(start);
            var point2 = ToUnit(end);
            var delta = point2 - point1;
            var lengthSquared = (delta.X * delta.X) + (delta.Y * delta.Y);
            var scaleFactor = Math.Sqrt(Math.Max((1 / lengthSquared) - 0.25, 0));
            if (sweep == largeArc)
            {
                scaleFactor = -scaleFactor;
            }

            delta *= scaleFactor;
            var center = new SvgPoint(((point1.X + point2.X) / 2) - delta.Y, ((point1.Y + point2.Y) / 2) + delta.X);

            var theta1 = Math.Atan2(point1.Y - center.Y, point1.X - center.X);
            var theta2 = Math.Atan2(point2.Y - center.Y, point2.X - center.X);
            var sweepAngle = theta2 - theta1;
            if (sweepAngle < 0 && sweep)
            {
                sweepAngle += 2 * Math.PI;
            }
            else if (sweepAngle > 0 && !sweep)
            {
                sweepAngle -= 2 * Math.PI;
            }

            var userCenter = new SvgPoint((cos * center.X * rx) - (sin * center.Y * ry), (sin * center.X * rx) + (cos * center.Y * ry));
            arc = new SvgArc(userCenter, rx, ry, cos, sin, theta1, sweepAngle);
            return true;
        }
    }

    /// <summary>
    /// Converts the commands to absolute M, L, C and Z segments (https://svgwg.org/svg2-draft/paths.html). When
    /// <paramref name="keepArcs"/> is true, the arcs are A segments instead of cubic Béziers.
    /// </summary>
    internal sealed class Normalizer(bool keepArcs = false)
    {
        private SvgPoint _current;
        private SvgPoint _subpathStart;
        private SvgPoint _controlPoint;
        private char _lastCommand;

        public void Emit<TState>(SvgPathCommand command, Action<TState, NormalizedSegment> emit, TState state)
        {
            var v = command.Values;
            var relative = char.IsAsciiLetterLower(command.Type);
            var origin = relative ? _current : default;
            SvgPoint Point(int index) => new SvgPoint(v[index], v[index + 1]) + origin;

            SvgPoint target;
            switch (char.ToUpperInvariant(command.Type))
            {
                case 'M':
                    target = Point(0);
                    _subpathStart = target;
                    emit(state, new NormalizedSegment('M', default, default, target));
                    break;

                case 'L':
                    target = Point(0);
                    emit(state, new NormalizedSegment('L', default, default, target));
                    break;

                case 'H':
                    target = new SvgPoint(v[0] + origin.X, _current.Y);
                    emit(state, new NormalizedSegment('L', default, default, target));
                    break;

                case 'V':
                    target = new SvgPoint(_current.X, v[0] + origin.Y);
                    emit(state, new NormalizedSegment('L', default, default, target));
                    break;

                case 'Z':
                    target = _subpathStart;
                    emit(state, new NormalizedSegment('Z', default, default, target));
                    break;

                case 'C':
                case 'S':
                {
                    SvgPoint point1, point2;
                    if (char.ToUpperInvariant(command.Type) == 'C')
                    {
                        point1 = Point(0);
                        point2 = Point(2);
                        target = Point(4);
                    }
                    else
                    {
                        // The first control point is the reflection of the second control point of the previous cubic Bézier
                        point1 = IsCubic(_lastCommand) ? (_current * 2) - _controlPoint : _current;
                        point2 = Point(0);
                        target = Point(2);
                    }

                    _controlPoint = point2;
                    emit(state, new NormalizedSegment('C', point1, point2, target));
                    break;
                }

                case 'Q':
                case 'T':
                {
                    SvgPoint control;
                    if (char.ToUpperInvariant(command.Type) == 'Q')
                    {
                        control = Point(0);
                        target = Point(2);
                    }
                    else
                    {
                        control = IsQuadratic(_lastCommand) ? (_current * 2) - _controlPoint : _current;
                        target = Point(0);
                    }

                    // The cubic Bézier equivalent to the quadratic Bézier
                    _controlPoint = control;
                    var point1 = _current + ((control - _current) * (2.0 / 3));
                    var point2 = target + ((control - target) * (2.0 / 3));
                    emit(state, new NormalizedSegment('C', point1, point2, target));
                    break;
                }

                case 'A':
                    target = Point(5);
                    if (!SvgArc.TryCreate(_current, v[0], v[1], v[2], v[3] != 0, v[4] != 0, target, out var arc))
                    {
                        // An arc with a zero radius, or whose endpoints are identical, is a straight line
                        emit(state, new NormalizedSegment('L', default, default, target));
                    }
                    else if (keepArcs)
                    {
                        emit(state, new NormalizedSegment('A', default, default, target, arc));
                    }
                    else
                    {
                        EmitCubics(arc, target, emit, state);
                    }

                    break;

                default:
                    throw new InvalidOperationException("Unknown path command " + command.Type);
            }

            _current = target;
            if (!IsCubic(command.Type) && !IsQuadratic(command.Type))
            {
                _controlPoint = _current;
            }

            _lastCommand = command.Type;
        }

        private static bool IsCubic(char command) => command is 'C' or 'c' or 'S' or 's';

        private static bool IsQuadratic(char command) => command is 'Q' or 'q' or 'T' or 't';

        /// <summary>Approximates an elliptical arc with cubic Béziers, each spanning at most 90 degrees.</summary>
        private static void EmitCubics<TState>(SvgArc arc, SvgPoint end, Action<TState, NormalizedSegment> emit, TState state)
        {
            // The derivative of the point with respect to the angle
            SvgPoint Tangent(double theta)
            {
                var x = -arc.RadiusX * Math.Sin(theta);
                var y = arc.RadiusY * Math.Cos(theta);
                return new SvgPoint((arc.Cos * x) - (arc.Sin * y), (arc.Sin * x) + (arc.Cos * y));
            }

            // The tolerance avoids an extra segment for the rounding errors of atan2
            var segments = (int)Math.Ceiling(Math.Abs(arc.SweepAngle / ((Math.PI / 2) + 0.001)));
            for (var i = 0; i < segments; i++)
            {
                var startTheta = arc.StartAngle + (i * arc.SweepAngle / segments);
                var endTheta = arc.StartAngle + ((i + 1) * arc.SweepAngle / segments);
                var t = 4.0 / 3 * Math.Tan((endTheta - startTheta) / 4);
                var control1 = arc.PointAt(startTheta) + (Tangent(startTheta) * t);
                var control2 = arc.PointAt(endTheta) - (Tangent(endTheta) * t);

                // The last segment ends exactly at the end point of the arc
                var target = i == segments - 1 ? end : arc.PointAt(endTheta);
                emit(state, new NormalizedSegment('C', control1, control2, target));
            }
        }
    }
}
