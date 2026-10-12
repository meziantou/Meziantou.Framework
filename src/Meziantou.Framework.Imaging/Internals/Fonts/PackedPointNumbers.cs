namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Reads the packed point numbers of a tuple variation: the points its deltas are for, in increasing order.</summary>
/// <remarks>
/// "Packed point numbers" of the tuple variation store. The count comes first, on one or two bytes; a count of zero
/// stands for every point of the glyph. The numbers follow as runs of differences with the previous number, of one or
/// two bytes each.
/// </remarks>
internal ref struct PackedPointNumbers
{
    private const int PointsAreWords = 0x80;
    private const int RunCountMask = 0x7F;

    private readonly ReadOnlySpan<byte> _data;
    private int _position;
    private int _runRemaining;
    private bool _runIsWords;
    private int _point;

    public PackedPointNumbers(ReadOnlySpan<byte> data, int position)
    {
        _data = data;
        Count = ReadCount(data, ref position);
        _position = position;
    }

    /// <summary>Gets a value indicating whether the deltas are for every point of the glyph: no number is given. A default value says so.</summary>
    public readonly bool IsAllPoints => Count == 0;

    /// <summary>Gets the number of point numbers.</summary>
    public int Count { get; }

    /// <summary>Reads the next point number.</summary>
    public int Read()
    {
        if (_runRemaining == 0)
        {
            var control = FontBinary.UInt8(_data, _position++);
            _runIsWords = (control & PointsAreWords) != 0;
            _runRemaining = (control & RunCountMask) + 1;
        }

        _runRemaining--;
        if (_runIsWords)
        {
            _point += FontBinary.UInt16(_data, _position);
            _position += 2;
        }
        else
        {
            _point += FontBinary.UInt8(_data, _position++);
        }

        return _point;
    }

    /// <summary>Gets the position that follows packed point numbers.</summary>
    public static int Skip(ReadOnlySpan<byte> data, int position)
    {
        var count = ReadCount(data, ref position);
        while (count > 0)
        {
            var control = FontBinary.UInt8(data, position++);
            var run = (control & RunCountMask) + 1;
            position += (control & PointsAreWords) != 0 ? run * 2 : run;
            count -= run;
        }

        FontBinary.EnsureRange(data, position, 0);
        return position;
    }

    private static int ReadCount(ReadOnlySpan<byte> data, ref int position)
    {
        var count = FontBinary.UInt8(data, position++);
        if ((count & 0x80) != 0)
        {
            count = ((count & 0x7F) << 8) | FontBinary.UInt8(data, position++);
        }

        return count;
    }
}
