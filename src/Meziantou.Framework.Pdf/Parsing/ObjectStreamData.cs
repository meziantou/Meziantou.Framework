namespace Meziantou.Framework.Pdf.Parsing;

/// <summary>The decoded data of an object stream (ISO 32000-2, 7.5.7): the numbers and offsets of its objects.</summary>
internal sealed class ObjectStreamData(byte[] data, int first, (int Number, int Offset)[] objects)
{
    public byte[] Data { get; } = data;

    public int First { get; } = first;

    public (int Number, int Offset)[] Objects { get; } = objects;
}
