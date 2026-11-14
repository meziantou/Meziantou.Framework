using System.Buffers.Binary;

namespace Meziantou.Framework.Fonts;

/// <summary>
/// Writes an outline as a Type 2 charstring (Adobe Technical Note #5177): <c>rmoveto</c>, <c>rlineto</c>,
/// <c>rrcurveto</c> and <c>endchar</c>, with the width as the first operand. A move is written when a line or a curve
/// follows it; a contour is closed by the next move, as Type 2 charstrings close them.
/// </summary>
/// <remarks>
/// The points are rounded to the 16.16 fixed-point numbers of the format, and each point is written relative to the
/// rounded previous one: the decoded points are exactly the rounded points. The charstrings of the glyphs follow each
/// other in a <see cref="FontByteWriter"/>, whose storage is rented from the allocation scope of the conversion.
/// </remarks>
internal sealed class Type2CharstringWriter(FontByteWriter output)
{
    private const int RLineTo = 5;
    private const int RRCurveTo = 8;
    private const int EndChar = 14;
    private const int RMoveTo = 21;
    private const long One = 65536;

    private long _currentX;
    private long _currentY;
    private long _writtenX;
    private long _writtenY;
    private bool _isContourOpen;
    private int _start;

    /// <summary>Gets or sets the width, the first operand of the charstring when it is not 0 (the nominal width); set before the first operator is written.</summary>
    public double Width { get; set; }

    /// <summary>Gets a value indicating whether an operator of the charstring is written.</summary>
    public bool HasStarted { get; private set; }

    /// <summary>Gets the length of the charstring of the glyph.</summary>
    public int Length => output.Length - _start;

    /// <summary>Gets a value indicating whether the outline has a line or a curve.</summary>
    public bool HasPoints { get; private set; }

    /// <summary>Gets the smallest x of the points written, the control points included.</summary>
    public double XMin { get; private set; }

    /// <summary>Gets the largest x of the points written, the control points included.</summary>
    public double XMax { get; private set; }

    /// <summary>Gets the smallest y of the points written, the control points included.</summary>
    public double YMin { get; private set; }

    /// <summary>Gets the largest y of the points written, the control points included.</summary>
    public double YMax { get; private set; }

    /// <summary>Starts the charstring of a glyph, after the ones written before.</summary>
    public void Begin()
    {
        _start = output.Length;
        Width = 0;
        HasStarted = false;
        _currentX = 0;
        _currentY = 0;
        _writtenX = 0;
        _writtenY = 0;
        _isContourOpen = false;
        HasPoints = false;
        XMin = XMax = YMin = YMax = 0;
    }

    /// <summary>Moves the current point; the next line or curve starts a contour there.</summary>
    public void MoveTo(double x, double y)
    {
        _currentX = ToFixed(x);
        _currentY = ToFixed(y);
        _isContourOpen = false;
    }

    /// <summary>Ends the contour; the current point does not move.</summary>
    public void ClosePath() => _isContourOpen = false;

    public void LineTo(double x, double y)
    {
        OpenContour();
        var (fixedX, fixedY) = (ToFixed(x), ToFixed(y));
        WritePoint(fixedX, fixedY);
        WriteOperator(RLineTo);
        _currentX = fixedX;
        _currentY = fixedY;
    }

    public void CurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
    {
        OpenContour();
        WritePoint(ToFixed(x1), ToFixed(y1));
        WritePoint(ToFixed(x2), ToFixed(y2));
        var (fixedX, fixedY) = (ToFixed(x3), ToFixed(y3));
        WritePoint(fixedX, fixedY);
        WriteOperator(RRCurveTo);
        _currentX = fixedX;
        _currentY = fixedY;
    }

    /// <summary>Ends the charstring.</summary>
    public void Finish()
    {
        WriteWidth();
        WriteOperator(EndChar);
    }

    private void OpenContour()
    {
        if (_isContourOpen)
            return;

        WriteWidth();
        WritePoint(_currentX, _currentY);
        WriteOperator(RMoveTo);
        _isContourOpen = true;
    }

    private void WriteWidth()
    {
        if (!HasStarted && Width != 0)
        {
            WriteNumber(ToFixed(Width));
        }

        HasStarted = true;
    }

    private void WritePoint(long x, long y)
    {
        WriteNumber(x - _writtenX);
        WriteNumber(y - _writtenY);
        _writtenX = x;
        _writtenY = y;

        var pointX = x / (double)One;
        var pointY = y / (double)One;
        if (!HasPoints)
        {
            XMin = XMax = pointX;
            YMin = YMax = pointY;
            HasPoints = true;
        }
        else
        {
            XMin = Math.Min(XMin, pointX);
            XMax = Math.Max(XMax, pointX);
            YMin = Math.Min(YMin, pointY);
            YMax = Math.Max(YMax, pointY);
        }
    }

    private void WriteOperator(int value) => output.Append(1)[0] = (byte)value;

    /// <summary>Writes a number given in 16.16 fixed point (table 3): an integer in its shortest form, a fraction as 255 and 32 bits.</summary>
    private void WriteNumber(long value)
    {
        Span<byte> bytes = stackalloc byte[5];
        int length;
        if (value % One == 0 && value / One is >= -32768 and <= 32767)
        {
            var integer = (int)(value / One);
            switch (integer)
            {
                case >= -107 and <= 107:
                    bytes[0] = (byte)(integer + 139);
                    length = 1;
                    break;
                case >= 108 and <= 1131:
                    integer -= 108;
                    bytes[0] = (byte)((integer >> 8) + 247);
                    bytes[1] = (byte)integer;
                    length = 2;
                    break;
                case >= -1131 and <= -108:
                    integer = -integer - 108;
                    bytes[0] = (byte)((integer >> 8) + 251);
                    bytes[1] = (byte)integer;
                    length = 2;
                    break;
                default:
                    bytes[0] = 28;
                    bytes[1] = (byte)(integer >> 8);
                    bytes[2] = (byte)integer;
                    length = 3;
                    break;
            }
        }
        else
        {
            if (value is < int.MinValue or > int.MaxValue)
                throw FontErrors.InvalidType1("a glyph has two points farther apart than a CFF charstring can describe (32,768 units).");

            bytes[0] = 255;
            BinaryPrimitives.WriteInt32BigEndian(bytes[1..], (int)value);
            length = 5;
        }

        bytes[..length].CopyTo(output.Append(length));
    }

    private static long ToFixed(double value)
    {
        // Written so that a value that is not a number fails the test
        if (!(Math.Abs(value) <= FontErrors.MaxCoordinate))
            throw FontErrors.Invalid("a glyph has a point outside the coordinates an outline can have.");

        return (long)Math.Round(value * One, MidpointRounding.AwayFromZero);
    }
}
