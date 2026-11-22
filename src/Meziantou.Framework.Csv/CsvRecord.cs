using System.Runtime.CompilerServices;
using Meziantou.Framework.Csv.Internals;

namespace Meziantou.Framework.Csv;

/// <summary>A record read by <see cref="CsvReader"/>. It is valid until the next record is read.</summary>
public readonly ref struct CsvRecord
{
    private readonly CsvParser<char> _parser;
    private readonly ReadOnlySpan<char> _row;
    private readonly ReadOnlySpan<int> _columnEnds;
    private readonly bool _isPlain;

    internal CsvRecord(CsvParser<char> parser)
    {
        _parser = parser;
        _row = parser.RowSpan;
        _columnEnds = parser.RowColumnEnds;
        _isPlain = parser.RowIsPlain;
    }

    /// <summary>Gets the number of columns of the record. An empty line has no column.</summary>
    public int ColumnCount => _columnEnds.Length - 1;

    /// <summary>Gets the text of the record, as it is in the data, without its line terminator.</summary>
    public ReadOnlySpan<char> RawSpan => _row;

    /// <summary>Gets the 0-based index of the record. The header row and the comment lines are not counted.</summary>
    public long RowIndex => _parser.RecordIndex;

    /// <summary>Gets the 1-based line number where the record starts.</summary>
    public long LineNumber => _parser.LineNumber;

    /// <summary>Gets the value of a column.</summary>
    /// <param name="index">The 0-based index of the column.</param>
    /// <exception cref="ArgumentOutOfRangeException">The record has no column at <paramref name="index"/>.</exception>
    public CsvField this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var columnEnds = _columnEnds;
            if ((uint)index >= (uint)(columnEnds.Length - 1))
                ThrowIndexOutOfRange(index, columnEnds.Length - 1);

            var start = columnEnds[index] + 1;
            var raw = _row.Slice(start, columnEnds[index + 1] - start);
            if (_isPlain)
                return new CsvField(_parser, index, raw, raw, isQuoted: false);

            _parser.GetValue(index, raw, out var value, out var isQuoted);
            return new CsvField(_parser, index, value, raw, isQuoted);
        }
    }

    /// <summary>Gets the value of a column.</summary>
    /// <param name="index">The index of the column, which can be from the end, such as <c>^1</c> for the last column.</param>
    /// <exception cref="ArgumentOutOfRangeException">The record has no column at <paramref name="index"/>.</exception>
    public CsvField this[Index index] => this[index.GetOffset(ColumnCount)];

    /// <summary>Gets the value of the column with the specified name in the header row.</summary>
    /// <param name="columnName">The name of the column.</param>
    /// <exception cref="InvalidOperationException">The reader has no header row.</exception>
    /// <exception cref="KeyNotFoundException">The header row has no column with this name.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The record is shorter than the header row and has no value for this column.</exception>
    public CsvField this[string columnName]
    {
        get
        {
            if (!_parser.TryGetColumnIndex(columnName, out var index))
                throw new KeyNotFoundException($"The header row has no column named '{columnName}'.");

            return this[index];
        }
    }

    /// <summary>Gets the value of the column with the specified name in the header row.</summary>
    /// <param name="columnName">The name of the column.</param>
    /// <param name="field">The value of the column.</param>
    /// <returns><see langword="true"/> when the header row has a column with this name and the record has a value for it; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="InvalidOperationException">The reader has no header row.</exception>
    public bool TryGetField(string columnName, out CsvField field)
    {
        if (_parser.TryGetColumnIndex(columnName, out var index) && index < ColumnCount)
        {
            field = this[index];
            return true;
        }

        field = default;
        return false;
    }

    /// <summary>Gets the values of all the columns as strings.</summary>
    /// <returns>The values of the record.</returns>
    public string[] ToStringArray()
    {
        var result = new string[ColumnCount];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = this[i].ToString();
        }

        return result;
    }

    /// <summary>Gets the text of the record, as it is in the data.</summary>
    /// <returns>The text of the record.</returns>
    public override string ToString() => new(RawSpan);

    [DoesNotReturn]
    private static void ThrowIndexOutOfRange(int index, int columnCount)
    {
        throw new ArgumentOutOfRangeException(nameof(index), index, $"The record has {columnCount.ToString(CultureInfo.InvariantCulture)} columns.");
    }
}
