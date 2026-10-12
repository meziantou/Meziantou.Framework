namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Reads the packed deltas of a tuple variation.</summary>
/// <remarks>
/// "Packed deltas" of the tuple variation store: runs of zeros, which take no byte, and of values of one, two or four
/// bytes each.
/// </remarks>
internal ref struct PackedDeltas
{
    private const int DeltasAreZero = 0x80;
    private const int DeltasAreWords = 0x40;
    private const int RunCountMask = 0x3F;

    private readonly ReadOnlySpan<byte> _data;
    private int _position;
    private int _runRemaining;
    private int _runSize;

    public PackedDeltas(ReadOnlySpan<byte> data, int position)
    {
        _data = data;
        _position = position;
    }

    /// <summary>Reads the next delta.</summary>
    public int Read()
    {
        if (_runRemaining == 0)
        {
            var control = FontBinary.UInt8(_data, _position++);
            _runSize = GetSize(control);
            _runRemaining = (control & RunCountMask) + 1;
        }

        _runRemaining--;
        _position += _runSize;
        return _runSize switch
        {
            0 => 0,
            1 => FontBinary.Int8(_data, _position - 1),
            2 => FontBinary.Int16(_data, _position - 2),
            _ => FontBinary.Int32(_data, _position - 4),
        };
    }

    /// <summary>Gets the position that follows a number of packed deltas; a run that goes beyond them is skipped whole.</summary>
    public static int Skip(ReadOnlySpan<byte> data, int position, int count)
    {
        while (count > 0)
        {
            var control = FontBinary.UInt8(data, position++);
            var run = (control & RunCountMask) + 1;
            position += run * GetSize(control);
            count -= run;
        }

        FontBinary.EnsureRange(data, position, 0);
        return position;
    }

    // Zeros, bytes, words, or - with both flags - 32-bit values.
    private static int GetSize(int control) => (control & (DeltasAreZero | DeltasAreWords)) switch
    {
        DeltasAreZero => 0,
        DeltasAreWords => 2,
        DeltasAreZero | DeltasAreWords => 4,
        _ => 1,
    };
}
