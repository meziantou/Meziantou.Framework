namespace Meziantou.Framework.Bencode;

public sealed class BencodeInteger : BencodeValue
{
    public BencodeInteger(long value)
    {
        Value = value;
    }

    public override BencodeValueKind Kind => BencodeValueKind.Integer;

    public long Value { get; }
}
