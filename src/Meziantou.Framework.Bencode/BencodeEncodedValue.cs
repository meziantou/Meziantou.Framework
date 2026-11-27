namespace Meziantou.Framework.Bencode;

/// <summary>A value written as the bytes it is already encoded to, whatever the order of the keys requested to the writer.</summary>
internal sealed class BencodeEncodedValue : BencodeValue
{
    private readonly ReadOnlyMemory<byte> _encodedValue;

    public BencodeEncodedValue(ReadOnlyMemory<byte> encodedValue, BencodeValueKind kind)
    {
        _encodedValue = encodedValue;
        Kind = kind;
    }

    public override BencodeValueKind Kind { get; }

    public override void WriteTo(BencodeWriter writer, bool canonical)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteEncodedValue(_encodedValue.Span);
    }
}
