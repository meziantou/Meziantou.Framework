namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A <c>pclr</c> box (T.800, I.5.3.4): the palette that turns the samples of a component into the samples of several channels.</summary>
internal sealed class Jpeg2000Palette
{
    private readonly ushort[] _entries;
    private readonly byte[] _columnDepths;

    public Jpeg2000Palette(int entryCount, byte[] columnDepths, ushort[] entries)
    {
        EntryCount = entryCount;
        _columnDepths = columnDepths;
        _entries = entries;
    }

    /// <summary>Gets the number of entries, 1 to 1024.</summary>
    public int EntryCount { get; }

    /// <summary>Gets the number of columns: the channels the palette generates.</summary>
    public int ColumnCount => _columnDepths.Length;

    /// <summary>Gets the number of bits of the values of a column.</summary>
    public int GetColumnPrecision(int column) => (_columnDepths[column] & 0x7F) + 1;

    /// <summary>Gets a value indicating whether the values of a column are signed.</summary>
    public bool IsColumnSigned(int column) => (_columnDepths[column] & 0x80) != 0;

    /// <summary>Gets the values of a column, one per entry, when the column has at most 16 bits.</summary>
    public ReadOnlySpan<ushort> GetColumn(int column) => _entries.AsSpan(column * EntryCount, EntryCount);
}
