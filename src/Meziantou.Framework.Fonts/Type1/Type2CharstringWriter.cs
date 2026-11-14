namespace Meziantou.Framework.Fonts;

/// <summary>
/// Writes an outline as a Type 2 charstring (Adobe Technical Note #5177): <c>rmoveto</c>, <c>rlineto</c>,
/// <c>rrcurveto</c> and <c>endchar</c>, with the width as the first operand. A move is written when a line or a curve
/// follows it; a contour is closed by the next move, as Type 2 charstrings close them.
/// </summary>
/// <remarks>
/// The points are rounded to the 16.16 fixed-point numbers of the format, and each point is written relative to the
/// rounded previous one: the decoded points are exactly the rounded points.
/// </remarks>
internal sealed class Type2CharstringWriter
{
    private const int RLineTo = 5;
    private const int RRCurveTo = 8;
    private const int EndChar = 14;
    private const int RMoveTo = 21;
    private const long One = 65536;

    private readonly List<byte> _bytes = [];
    private long _currentX;
    private long _currentY;
    private long _writtenX;
    private long _writtenY;
    private bool _isContourOpen;

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

    /// <summary>Starts a glyph.</summary>
    public void Reset()
    {
        _bytes.Clear();
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

    /// <summary>Ends the charstring and gets its bytes; the width is the first operand when it is not 0 (the nominal width).</summary>
    public byte[] Finish(double width)
    {
        WriteOperator(EndChar);
        if (width == 0)
            return [.. _bytes];

        var widthBytes = new List<byte>(5);
        WriteNumber(widthBytes, ToFixed(width));
        return [.. widthBytes, .. _bytes];
    }

    private void OpenContour()
    {
        if (_isContourOpen)
            return;

        WritePoint(_currentX, _currentY);
        WriteOperator(RMoveTo);
        _isContourOpen = true;
    }

    private void WritePoint(long x, long y)
    {
        WriteNumber(_bytes, x - _writtenX);
        WriteNumber(_bytes, y - _writtenY);
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

    private void WriteOperator(int value) => _bytes.Add((byte)value);

    /// <summary>Writes a number given in 16.16 fixed point (table 3): an integer in its shortest form, a fraction as 255 and 32 bits.</summary>
    private static void WriteNumber(List<byte> bytes, long value)
    {
        if (value % One == 0 && value / One is >= -32768 and <= 32767)
        {
            var integer = (int)(value / One);
            switch (integer)
            {
                case >= -107 and <= 107:
                    bytes.Add((byte)(integer + 139));
                    break;
                case >= 108 and <= 1131:
                    integer -= 108;
                    bytes.Add((byte)((integer >> 8) + 247));
                    bytes.Add((byte)integer);
                    break;
                case >= -1131 and <= -108:
                    integer = -integer - 108;
                    bytes.Add((byte)((integer >> 8) + 251));
                    bytes.Add((byte)integer);
                    break;
                default:
                    bytes.Add(28);
                    bytes.Add((byte)(integer >> 8));
                    bytes.Add((byte)integer);
                    break;
            }

            return;
        }

        if (value is < int.MinValue or > int.MaxValue)
            throw FontErrors.InvalidType1("a glyph has two points farther apart than a CFF charstring can describe (32,768 units).");

        var fixedValue = (int)value;
        bytes.Add(255);
        bytes.Add((byte)(fixedValue >> 24));
        bytes.Add((byte)(fixedValue >> 16));
        bytes.Add((byte)(fixedValue >> 8));
        bytes.Add((byte)fixedValue);
    }

    private static long ToFixed(double value)
    {
        // Written so that a value that is not a number fails the test
        if (!(Math.Abs(value) <= FontErrors.MaxCoordinate))
            throw FontErrors.Invalid("a glyph has a point outside the coordinates an outline can have.");

        return (long)Math.Round(value * One, MidpointRounding.AwayFromZero);
    }
}
