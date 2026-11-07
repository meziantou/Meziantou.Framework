using System.Text;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Reads and writes a path as the path data of SVG (the <c>d</c> attribute of a <c>path</c> element).</summary>
/// <remarks>
/// <para>
/// The reader follows the grammar of SVG: the commands <c>M L H V C S Q T A Z</c> and their relative forms, numbers
/// with an optional sign, fraction and exponent, separated by white space or a comma or by nothing where the grammar
/// leaves no doubt (<c>1-2</c>, <c>.5.5</c>, the flags of an arc), and the repetition of the arguments of a command
/// without its letter (after a <c>M</c>, they are those of a <c>L</c>). Unlike a renderer of SVG, which draws what
/// comes before an error, the reader rejects the whole text.
/// </para>
/// <para>
/// A number is converted by the runtime, which rounds it correctly on every platform. An arc is added as
/// <see cref="VectorPathBuilder.ArcTo(double, double, double, bool, bool, double, double)"/> adds it.
/// </para>
/// </remarks>
internal static class SvgPathData
{
    /// <summary>Reads path data.</summary>
    /// <param name="text">The path data.</param>
    /// <param name="path">The path.</param>
    /// <param name="position">The index of the character the error was found at.</param>
    /// <param name="error">What is wrong at that position.</param>
    /// <returns><see langword="false"/> when the text is not valid path data, or describes a path the library does not accept.</returns>
    public static bool TryParse(ReadOnlySpan<char> text, [NotNullWhen(true)] out VectorPath? path, out int position, [NotNullWhen(false)] out string? error)
    {
        path = null;
        var reader = new Reader(text);
        var builder = new VectorPathBuilder();
        double x = 0, y = 0, startX = 0, startY = 0;

        // The control point the next smooth curve mirrors, and the kind of curve it belongs to
        double controlX = 0, controlY = 0;
        var previous = '\0';
        var hasContour = false;
        reader.SkipWhiteSpace();
        while (!reader.IsAtEnd)
        {
            position = reader.Position;
            var command = reader.ReadCommand();
            if (command == '\0')
            {
                error = "A command is expected.";
                return false;
            }

            if (!hasContour && command is not ('M' or 'm'))
            {
                error = "Path data must start with a 'M' or 'm' command.";
                return false;
            }

            var isRelative = char.IsAsciiLetterLower(command);
            var kind = char.ToUpperInvariant(command);
            if (kind is not ('M' or 'L' or 'H' or 'V' or 'C' or 'S' or 'Q' or 'T' or 'A' or 'Z'))
            {
                error = $"'{command}' is not a command of path data.";
                return false;
            }

            if (kind == 'Z')
            {
                builder.Close();
                x = startX;
                y = startY;
                previous = 'Z';
                reader.SkipWhiteSpace();
                continue;
            }

            // The arguments of the command, as many times as they are given
            var isFirst = true;
            while (true)
            {
                reader.SkipWhiteSpace();
                if (!isFirst)
                {
                    // Another group of arguments starts with a number, after an optional comma
                    var hadComma = reader.SkipComma();
                    if (!reader.IsAtNumber)
                    {
                        if (hadComma)
                        {
                            position = reader.Position;
                            error = "A number is expected after a comma.";
                            return false;
                        }

                        break;
                    }
                }

                position = reader.Position;
                var arguments = position;
                reader.BeginArguments();
                double x1, y1, x2, y2, endX, endY;
                switch (kind)
                {
                    case 'M' or 'L' or 'T':
                        if (!reader.TryReadPoint(isRelative, x, y, out endX, out endY, ref position))
                            goto InvalidNumber;

                        if (kind == 'M' && isFirst)
                        {
                            builder.AppendMoveTo(endX, endY);
                            startX = endX;
                            startY = endY;
                            hasContour = true;
                        }
                        else if (kind == 'T')
                        {
                            // The control point mirrors the one of the curve before, around the current point
                            (x1, y1) = previous is 'Q' ? ((2 * x) - controlX, (2 * y) - controlY) : (x, y);
                            if (!IsValid(x1) || !IsValid(y1))
                                goto InvalidNumber;

                            builder.QuadraticBezierTo(x1, y1, endX, endY);
                            controlX = x1;
                            controlY = y1;
                        }
                        else
                        {
                            builder.AppendLineTo(endX, endY);
                        }

                        break;

                    case 'H':
                        endY = y;
                        if (!reader.TryReadCoordinate(isRelative, x, out endX, ref position))
                            goto InvalidNumber;

                        builder.AppendLineTo(endX, endY);
                        break;

                    case 'V':
                        endX = x;
                        if (!reader.TryReadCoordinate(isRelative, y, out endY, ref position))
                            goto InvalidNumber;

                        builder.AppendLineTo(endX, endY);
                        break;

                    case 'Q':
                        if (!reader.TryReadPoint(isRelative, x, y, out x1, out y1, ref position) || !reader.TryReadPoint(isRelative, x, y, out endX, out endY, ref position))
                            goto InvalidNumber;

                        builder.QuadraticBezierTo(x1, y1, endX, endY);
                        controlX = x1;
                        controlY = y1;
                        break;

                    case 'C' or 'S':
                        if (kind == 'C')
                        {
                            if (!reader.TryReadPoint(isRelative, x, y, out x1, out y1, ref position))
                                goto InvalidNumber;
                        }
                        else
                        {
                            (x1, y1) = previous is 'C' ? ((2 * x) - controlX, (2 * y) - controlY) : (x, y);
                            if (!IsValid(x1) || !IsValid(y1))
                                goto InvalidNumber;
                        }

                        if (!reader.TryReadPoint(isRelative, x, y, out x2, out y2, ref position) || !reader.TryReadPoint(isRelative, x, y, out endX, out endY, ref position))
                            goto InvalidNumber;

                        builder.CubicBezierTo(x1, y1, x2, y2, endX, endY);
                        controlX = x2;
                        controlY = y2;
                        break;

                    default:
                        if (!reader.TryReadNumber(out var radiusX, ref position) || !reader.TryReadNumber(out var radiusY, ref position) || !reader.TryReadNumber(out var rotation, ref position))
                            goto InvalidNumber;

                        if (!reader.TryReadFlag(out var isLargeArc, ref position) || !reader.TryReadFlag(out var isClockwise, ref position))
                        {
                            error = "A flag of an arc must be 0 or 1.";
                            return false;
                        }

                        if (!reader.TryReadPoint(isRelative, x, y, out endX, out endY, ref position))
                            goto InvalidNumber;

                        try
                        {
                            // A negative radius counts for its magnitude, as SVG says
                            builder.ArcTo(Math.Abs(radiusX), Math.Abs(radiusY), rotation, isLargeArc, isClockwise, endX, endY);
                        }
                        catch (ArgumentException)
                        {
                            position = arguments;
                            error = "The arc goes beyond the largest coordinate (16777216).";
                            return false;
                        }

                        break;
                }

                x = endX;
                y = endY;

                // The coordinates that follow a 'M' without a letter are those of a 'L': they mirror nothing
                previous = kind switch
                {
                    'Q' or 'T' => 'Q',
                    'C' or 'S' => 'C',
                    _ => kind,
                };
                isFirst = false;
            }
        }

        position = -1;
        error = null;
        path = builder.Build();
        return true;

    InvalidNumber:
        error = "A number is expected: finite, and no larger than 16777216 in magnitude once it is a coordinate.";
        return false;
    }

    /// <summary>Writes a path as path data, with absolute coordinates.</summary>
    public static string Format(VectorPath path)
    {
        var builder = new StringBuilder();
        foreach (var segment in path.EnumerateSegments())
        {
            if (builder.Length != 0)
            {
                builder.Append(' ');
            }

            var point = segment.Point;
            switch (segment.Kind)
            {
                case PathSegmentKind.MoveTo:
                    builder.Append(CultureInfo.InvariantCulture, $"M{point.X} {point.Y}");
                    break;

                case PathSegmentKind.LineTo:
                    builder.Append(CultureInfo.InvariantCulture, $"L{point.X} {point.Y}");
                    break;

                case PathSegmentKind.QuadraticBezierTo:
                    builder.Append(CultureInfo.InvariantCulture, $"Q{segment.Control1.X} {segment.Control1.Y} {point.X} {point.Y}");
                    break;

                case PathSegmentKind.CubicBezierTo:
                    builder.Append(CultureInfo.InvariantCulture, $"C{segment.Control1.X} {segment.Control1.Y} {segment.Control2.X} {segment.Control2.Y} {point.X} {point.Y}");
                    break;

                case PathSegmentKind.ArcTo:
                    builder.Append(CultureInfo.InvariantCulture, $"A{segment.RadiusX} {segment.RadiusY} {segment.Rotation} {(segment.IsLargeArc ? 1 : 0)} {(segment.IsClockwise ? 1 : 0)} {point.X} {point.Y}");
                    break;

                default:
                    builder.Append('Z');
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>Gets a value indicating whether a number can be a coordinate of a path. Written so that a value that is not a number is rejected too.</summary>
    private static bool IsValid(double value) => Math.Abs(value) <= DrawingGuard.MaxCoordinate;

    private ref struct Reader(ReadOnlySpan<char> text)
    {
        private readonly ReadOnlySpan<char> _text = text;
        private bool _hasArgument;

        public int Position { get; private set; }

        public readonly bool IsAtEnd => Position >= _text.Length;

        /// <summary>Gets a value indicating whether a number starts at the current position.</summary>
        public readonly bool IsAtNumber
        {
            get
            {
                if (IsAtEnd)
                    return false;

                var c = _text[Position];
                return char.IsAsciiDigit(c) || c is '+' or '-' or '.';
            }
        }

        public void SkipWhiteSpace()
        {
            while (!IsAtEnd && _text[Position] is ' ' or '\t' or '\n' or '\f' or '\r')
            {
                Position++;
            }
        }

        /// <summary>Skips a comma and the white space after it.</summary>
        /// <returns><see langword="true"/> when there was a comma.</returns>
        public bool SkipComma()
        {
            if (IsAtEnd || _text[Position] != ',')
                return false;

            Position++;
            SkipWhiteSpace();
            return true;
        }

        /// <summary>Reads the letter of a command.</summary>
        /// <returns>The letter, or <c>'\0'</c> when the character is not a letter.</returns>
        public char ReadCommand()
        {
            var c = _text[Position];
            if (!char.IsAsciiLetter(c))
                return '\0';

            Position++;
            return c;
        }

        /// <summary>Reads a number, with the separator before it when it is not the first argument of its group.</summary>
        /// <param name="value">The number.</param>
        /// <param name="errorPosition">Set to the position of the number when it is missing or is not finite.</param>
        public bool TryReadNumber(out double value, ref int errorPosition)
        {
            value = 0;
            SkipSeparator();
            var start = Position;
            errorPosition = start;
            var index = start;
            if (index < _text.Length && _text[index] is '+' or '-')
            {
                index++;
            }

            var digits = 0;
            while (index < _text.Length && char.IsAsciiDigit(_text[index]))
            {
                index++;
                digits++;
            }

            if (index < _text.Length && _text[index] == '.')
            {
                index++;
                while (index < _text.Length && char.IsAsciiDigit(_text[index]))
                {
                    index++;
                    digits++;
                }
            }

            if (digits == 0)
                return false;

            // An exponent, when a digit follows: otherwise the letter is not part of the number
            if (index < _text.Length && _text[index] is 'e' or 'E')
            {
                var exponent = index + 1;
                if (exponent < _text.Length && _text[exponent] is '+' or '-')
                {
                    exponent++;
                }

                if (exponent < _text.Length && char.IsAsciiDigit(_text[exponent]))
                {
                    while (exponent < _text.Length && char.IsAsciiDigit(_text[exponent]))
                    {
                        exponent++;
                    }

                    index = exponent;
                }
            }

            if (!double.TryParse(_text[start..index], NumberStyles.Float, CultureInfo.InvariantCulture, out value) || !double.IsFinite(value))
                return false;

            Position = index;
            return true;
        }

        /// <summary>Reads a coordinate, and adds the current one to it when the command is relative.</summary>
        public bool TryReadCoordinate(bool isRelative, double current, out double value, ref int errorPosition)
        {
            if (!TryReadNumber(out value, ref errorPosition))
                return false;

            if (isRelative)
            {
                value += current;
            }

            return IsValid(value);
        }

        public bool TryReadPoint(bool isRelative, double currentX, double currentY, out double x, out double y, ref int errorPosition)
        {
            y = 0;
            return TryReadCoordinate(isRelative, currentX, out x, ref errorPosition) && TryReadCoordinate(isRelative, currentY, out y, ref errorPosition);
        }

        /// <summary>Reads a flag of an arc: one character, which needs no separator after it.</summary>
        public bool TryReadFlag(out bool value, ref int errorPosition)
        {
            value = false;
            SkipSeparator();
            errorPosition = Position;
            if (IsAtEnd || _text[Position] is not ('0' or '1'))
                return false;

            value = _text[Position] == '1';
            Position++;
            return true;
        }

        /// <summary>Starts a group of arguments: nothing separates its first one from what comes before.</summary>
        public void BeginArguments() => _hasArgument = false;

        /// <summary>Skips what separates an argument from the one before it: white space, and one comma.</summary>
        private void SkipSeparator()
        {
            SkipWhiteSpace();
            if (_hasArgument)
            {
                SkipComma();
            }

            _hasArgument = true;
        }
    }
}
