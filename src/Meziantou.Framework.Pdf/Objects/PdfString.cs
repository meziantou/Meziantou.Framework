using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Pdf.Text;

namespace Meziantou.Framework.Pdf.Objects;

/// <summary>A string: a sequence of bytes (ISO 32000-2, 7.3.4). It is immutable.</summary>
/// <remarks>
/// A text string (7.9.2.2) is encoded with PDFDocEncoding, UTF-16BE or UTF-8, the last two with a byte order mark:
/// <see cref="FromText"/> and <see cref="ToText"/> convert it.
/// </remarks>
public sealed class PdfString : PdfObject, IEquatable<PdfString>
{
    private readonly byte[] _bytes;

    internal PdfString(byte[] bytes, bool isHexadecimal)
    {
        _bytes = bytes;
        IsHexadecimal = isHexadecimal;
    }

    /// <summary>Gets the bytes of the string.</summary>
    public ReadOnlyMemory<byte> Bytes => _bytes;

    /// <summary>Gets a value indicating whether the string is written in hexadecimal form (<c>&lt;48656C6C6F&gt;</c>) rather than as a literal (<c>(Hello)</c>).</summary>
    public bool IsHexadecimal { get; }

    /// <inheritdoc/>
    public override PdfObjectKind Kind => PdfObjectKind.String;

    internal ReadOnlySpan<byte> Span => _bytes;

    /// <summary>Creates a string from bytes.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <param name="hexadecimal">Whether the string is written in hexadecimal form.</param>
    /// <returns>The string.</returns>
    public static PdfString FromBytes(ReadOnlySpan<byte> bytes, bool hexadecimal = false) => new(bytes.ToArray(), hexadecimal);

    /// <summary>Creates a text string (ISO 32000-2, 7.9.2.2): PDFDocEncoding when every character has a code in it, otherwise UTF-16BE with a byte order mark.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The string.</returns>
    public static PdfString FromText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new PdfString(PdfTextStringEncoding.Encode(text), isHexadecimal: false);
    }

    /// <summary>Decodes the string as a text string (ISO 32000-2, 7.9.2.2): UTF-16BE or UTF-8 when it starts with their byte order mark, otherwise PDFDocEncoding.</summary>
    /// <returns>The text. A byte that has no character in PDFDocEncoding gives U+FFFD.</returns>
    public string ToText() => PdfTextStringEncoding.Decode(_bytes);

    /// <inheritdoc/>
    public bool Equals([NotNullWhen(true)] PdfString? other) => other is not null && _bytes.AsSpan().SequenceEqual(other._bytes);

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as PdfString);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(_bytes);
        return hash.ToHashCode();
    }
}
