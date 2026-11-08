namespace Meziantou.Framework.Fonts;

/// <summary>
/// An extended state table of the AAT tables: Apple TrueType Reference Manual, "Font Tables", "State Tables" and
/// "Extended State Tables". The class of a glyph, from a lookup table, and the current state select an entry; the entry
/// gives the next state and the action of the table that has the state table.
/// </summary>
/// <remarks>
/// The table says how many classes it has, not how many states: a state is valid as long as its row of the state array
/// and the entries it refers to are inside the table, which every read checks (<see cref="FontBinary"/>).
/// </remarks>
internal readonly ref struct AatStateTable
{
    /// <summary>The class of the end of the glyphs.</summary>
    public const int EndOfText = 0;

    /// <summary>The class of the glyphs the class table does not have.</summary>
    public const int OutOfBounds = 1;

    /// <summary>The class of a glyph a subtable deleted.</summary>
    public const int DeletedGlyph = 2;

    /// <summary>The state at the start of the glyphs.</summary>
    public const int StartOfText = 0;

    /// <summary>The glyph identifier of a deleted glyph.</summary>
    public const int Deleted = 0xFFFF;

    /// <summary>The size of the header: the number of classes and the offsets of the class table, the state array and the entry table.</summary>
    public const int HeaderLength = 16;

    private readonly int _classCount;
    private readonly int _stateArray;
    private readonly int _entryTable;
    private readonly int _entrySize;
    private readonly int _glyphCount;

    /// <summary>Initializes a new instance of the <see cref="AatStateTable"/> struct.</summary>
    /// <param name="table">The state table, from its header; the offsets of the header are from its start.</param>
    /// <param name="entrySize">The size of an entry, which the table that has the state table defines.</param>
    /// <param name="glyphCount">The number of glyphs of the font.</param>
    /// <exception cref="InvalidFontException">The header is malformed.</exception>
    public AatStateTable(ReadOnlySpan<byte> table, int entrySize, int glyphCount)
    {
        Data = table;
        _classCount = FontBinary.Offset32(table, 0);
        if (_classCount < 4 || _classCount > ushort.MaxValue)
            throw FontErrors.Invalid("a state table of an AAT table has an invalid number of classes.");

        var classTable = FontBinary.Offset32(table, 4);
        _stateArray = FontBinary.Offset32(table, 8);
        _entryTable = FontBinary.Offset32(table, 12);
        FontBinary.EnsureRange(table, _stateArray, (long)_classCount * 2);
        FontBinary.EnsureRange(table, _entryTable, entrySize);
        FontBinary.EnsureRange(table, classTable, 2);
        ClassTable = table[classTable..];
        AatLookup.Validate(ClassTable, glyphCount);
        _entrySize = entrySize;
        _glyphCount = glyphCount;
    }

    /// <summary>Gets the state table, from its header.</summary>
    public ReadOnlySpan<byte> Data { get; }

    private ReadOnlySpan<byte> ClassTable { get; }

    /// <summary>Gets the class of a glyph.</summary>
    public int GetClass(int glyph)
    {
        if (glyph == Deleted)
            return DeletedGlyph;

        return AatLookup.TryGetValue(ClassTable, glyph, _glyphCount, out var value) && value < _classCount ? value : OutOfBounds;
    }

    /// <summary>Gets the entry of a state and a class.</summary>
    /// <returns>The entry, from its first value (the next state); its size is the size of an entry.</returns>
    public ReadOnlySpan<byte> GetEntry(int state, int glyphClass)
    {
        var position = _stateArray + ((((long)state * _classCount) + glyphClass) * 2);
        var index = FontBinary.UInt16(FontBinary.Slice(Data, position, 2), 0);
        return FontBinary.Slice(Data, _entryTable + ((long)index * _entrySize), _entrySize);
    }
}
