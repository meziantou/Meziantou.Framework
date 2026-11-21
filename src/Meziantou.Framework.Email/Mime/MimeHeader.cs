using Meziantou.Framework.Email.Internals;

namespace Meziantou.Framework.Email;

/// <summary>A header field of a message or of a MIME part.</summary>
public sealed class MimeHeader
{
    internal MimeHeader(string name, string rawValue, byte[]? rawBytes)
    {
        Name = name;
        RawValue = rawValue;
        RawBytes = rawBytes;
    }

    /// <summary>Gets the name of the header, such as <c>Subject</c>.</summary>
    public string Name { get; }

    /// <summary>Gets the value as it is in the message: unfolded, with its encoded words ([RFC 2047]) and its comments.</summary>
    public string RawValue { get; }

    /// <summary>Gets the value with its encoded words decoded.</summary>
    public string Value => MimeEncoding.DecodeHeaderValue(RawValue);

    /// <summary>The bytes of the header as read, line breaks included, so that writing a message that was read keeps it unchanged.</summary>
    internal byte[]? RawBytes { get; }

    /// <summary>Creates a header whose value is a text: it is encoded ([RFC 2047]) when it is not ASCII.</summary>
    /// <param name="name">The name of the header.</param>
    /// <param name="value">The text.</param>
    /// <returns>The header.</returns>
    public static MimeHeader Create(string name, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);
        return new MimeHeader(name, MimeEncoding.EncodeHeaderText(value.ReplaceLineEndings(" ")), rawBytes: null);
    }

    /// <summary>Creates a header from a value that is already formatted, such as a list of addresses or a content type.</summary>
    /// <param name="name">The name of the header.</param>
    /// <param name="rawValue">The value, as it is written.</param>
    /// <returns>The header.</returns>
    public static MimeHeader CreateRaw(string name, string rawValue)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(rawValue);
        return new MimeHeader(name, rawValue.ReplaceLineEndings(" "), rawBytes: null);
    }

    /// <summary>Returns the name and the value of the header.</summary>
    /// <returns>The name and the value of the header.</returns>
    public override string ToString() => Name + ": " + Value;
}
