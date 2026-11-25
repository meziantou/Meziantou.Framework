using System.ComponentModel;
using Meziantou.Framework.Csv.Internals;

namespace Meziantou.Framework.Csv.Serialization.Metadata;

/// <summary>The values of the current record of UTF-8 data, for the members of <typeparamref name="T"/>. See <see cref="Utf8CsvMembersReader{T}"/>.</summary>
/// <typeparam name="T">The type whose members are set.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public ref struct Utf8CsvMemberValues<T>
{
    private readonly CsvParser<byte> _parser;
    private readonly ReadOnlySpan<byte> _row;
    private readonly ReadOnlySpan<int> _columnEnds;
    private readonly int[] _memberColumns;
    private readonly CsvPropertyInfo<T>[] _members;
    private readonly bool _isAscii;

    internal Utf8CsvMemberValues(CsvParser<byte> parser, int[] memberColumns, CsvPropertyInfo<T>[] members)
    {
        _parser = parser;
        _row = parser.RowSpan;
        _columnEnds = parser.RowColumnEnds;
        _memberColumns = memberColumns;
        _members = members;
        _isAscii = parser.RowIsAscii;
        Member = -1;
    }

    // See CsvMemberValues<T>.Member
    internal int Member { get; private set; }

    /// <summary>Converts the value of a member.</summary>
    /// <typeparam name="TValue">The type of the member.</typeparam>
    /// <param name="memberIndex">The index of the member in <see cref="CsvObjectInfoValues{T}.Properties"/>.</param>
    /// <param name="value">The value of the member.</param>
    /// <returns><see langword="true"/> when the record has a value for the member; otherwise, <see langword="false"/>, and the member keeps its value.</returns>
    public bool TryRead<TValue>(int memberIndex, out TValue value)
    {
        var column = _memberColumns[memberIndex];

        // -1 for a member without a column, which is above the columns of the record once unsigned
        if ((uint)column >= (uint)(_columnEnds.Length - 1))
        {
            value = default!;
            return false;
        }

        if (_members[memberIndex] is not CsvPropertyInfo<T, TValue> property)
        {
            CsvMemberValues.ThrowMemberTypeMismatch(typeof(T), memberIndex, typeof(TValue));
            value = default;
            return false;
        }

        Member = memberIndex;
        var start = _columnEnds[column] + 1;
        _parser.GetValue(column, _row[start.._columnEnds[column + 1]], out var text, out _);
        value = property.Read(text, _isAscii);
        return true;
    }
}
