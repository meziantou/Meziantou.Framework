namespace Meziantou.Framework.Pdf.Parsing;

/// <summary>The decoded data of an object stream (ISO 32000-2, 7.5.7): the numbers and offsets of its objects.</summary>
internal sealed class ObjectStreamData(byte[] data, int first, (int Number, int Offset)[] objects)
{
    private Dictionary<int, int>? _indexes;

    public byte[] Data { get; } = data;

    public int First { get; } = first;

    public (int Number, int Offset)[] Objects { get; } = objects;

    /// <summary>Finds the index of an object when the index of its cross-reference entry is wrong.</summary>
    /// <remarks>The table is built on the first miss, so that a damaged stream costs one pass over its objects, not one per object resolved.</remarks>
    public int IndexOf(int number)
    {
        if (_indexes is null)
        {
            _indexes = new Dictionary<int, int>(Objects.Length);
            for (var i = 0; i < Objects.Length; i++)
            {
                // The first occurrence wins, as the linear search did
                _indexes.TryAdd(Objects[i].Number, i);
            }
        }

        return _indexes.GetValueOrDefault(number, -1);
    }
}
