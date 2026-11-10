namespace Meziantou.Framework.Bencode;

/// <summary>The kind of token <see cref="BencodeReader"/> is positioned on.</summary>
[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "These names map to bencode primitives.")]
public enum BencodeTokenType
{
    /// <summary>No token: <see cref="BencodeReader.ReadAsync"/> was not called yet, or the data ended.</summary>
    None,

    /// <summary>An integer.</summary>
    Integer,

    /// <summary>A string that is a value.</summary>
    String,

    /// <summary>A string that is the key of a dictionary entry. The value of the entry is the next token.</summary>
    Key,

    /// <summary>The start of a list.</summary>
    StartList,

    /// <summary>The end of a list.</summary>
    EndList,

    /// <summary>The start of a dictionary.</summary>
    StartDictionary,

    /// <summary>The end of a dictionary.</summary>
    EndDictionary,
}
