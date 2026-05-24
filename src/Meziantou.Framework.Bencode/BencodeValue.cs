namespace Meziantou.Framework.Bencode;

public abstract class BencodeValue
{
    public abstract BencodeValueKind Kind { get; }

    public abstract void WriteTo(BencodeWriter writer, bool canonical);
}
