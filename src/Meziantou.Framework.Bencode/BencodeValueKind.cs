namespace Meziantou.Framework.Bencode;

[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "These names map to bencode primitives.")]
public enum BencodeValueKind
{
    Integer,
    String,
    List,
    Dictionary,
}
