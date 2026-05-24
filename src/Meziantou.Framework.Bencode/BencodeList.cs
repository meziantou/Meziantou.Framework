using System.Collections;

namespace Meziantou.Framework.Bencode;

public sealed class BencodeList : BencodeValue, IReadOnlyList<BencodeValue>
{
    private readonly List<BencodeValue> _values = [];

    public BencodeList()
    {
    }

    public BencodeList(IEnumerable<BencodeValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        foreach (var value in values)
        {
            Add(value);
        }
    }

    public override BencodeValueKind Kind => BencodeValueKind.List;

    public int Count => _values.Count;

    public BencodeValue this[int index] => _values[index];

    public void Add(BencodeValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _values.Add(value);
    }

    public IEnumerator<BencodeValue> GetEnumerator() => _values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
