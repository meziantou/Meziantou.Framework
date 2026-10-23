using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Meziantou.Framework.Pdf.Objects;

/// <summary>A name (ISO 32000-2, 7.3.5): an atomic symbol such as <c>/Type</c>. It is immutable and compared by its bytes.</summary>
/// <remarks>
/// A name is a sequence of bytes. <see cref="Value"/> is that sequence decoded as UTF-8, the recommended encoding
/// (7.3.5), or as ISO-8859-1 when it is not valid UTF-8; creating a name from a string encodes it as UTF-8.
/// </remarks>
public sealed class PdfName : PdfObject, IEquatable<PdfName>
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private string? _value;

    private PdfName(string key, string? value)
    {
        Key = key;
        _value = value;
    }

    /// <summary>Initializes a new instance of the <see cref="PdfName"/> class.</summary>
    /// <param name="value">The name, without the leading slash. It is encoded as UTF-8.</param>
    public PdfName(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Key = Encoding.Latin1.GetString(Encoding.UTF8.GetBytes(value));
        _value = value;
    }

    /// <summary>Gets the name, without the leading slash.</summary>
    public string Value => _value ??= DecodeValue(Key);

    /// <inheritdoc/>
    public override PdfObjectKind Kind => PdfObjectKind.Name;

    /// <summary>Gets the bytes of the name as ISO-8859-1 characters (one character per byte).</summary>
    internal string Key { get; }

    /// <summary>Converts a string to a name.</summary>
    /// <param name="value">The name, without the leading slash.</param>
    public static implicit operator PdfName(string value) => Create(value);

    /// <summary>Indicates whether two names are equal.</summary>
    /// <param name="left">The first name.</param>
    /// <param name="right">The second name.</param>
    /// <returns><see langword="true"/> when the names have the same bytes.</returns>
    public static bool operator ==(PdfName? left, PdfName? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Indicates whether two names are different.</summary>
    /// <param name="left">The first name.</param>
    /// <param name="right">The second name.</param>
    /// <returns><see langword="true"/> when the names have different bytes.</returns>
    public static bool operator !=(PdfName? left, PdfName? right) => !(left == right);

    /// <summary>Creates a name, or gets the shared instance of a name the library knows.</summary>
    /// <param name="value">The name, without the leading slash.</param>
    /// <returns>The name.</returns>
    public static PdfName Create(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (PdfNames.TryGet(value, out var name))
            return name;

        return new PdfName(value);
    }

    /// <summary>Creates a name from its bytes, the escape sequences already decoded.</summary>
    internal static PdfName FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length <= 64)
        {
            Span<char> chars = stackalloc char[bytes.Length];
            for (var i = 0; i < bytes.Length; i++)
            {
                chars[i] = (char)bytes[i];
            }

            if (PdfNames.TryGet(chars, out var known))
                return known;
        }

        return new PdfName(Encoding.Latin1.GetString(bytes), value: null);
    }

    internal static PdfName CreateWellKnown(string asciiValue) => new(asciiValue, asciiValue);

    /// <summary>Gets the bytes of the name.</summary>
    internal byte[] GetBytes() => Encoding.Latin1.GetBytes(Key);

    /// <inheritdoc/>
    public bool Equals([NotNullWhen(true)] PdfName? other) => other is not null && (ReferenceEquals(this, other) || string.Equals(Key, other.Key, StringComparison.Ordinal));

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as PdfName);

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Key);

    private static string DecodeValue(string key)
    {
        var bytes = Encoding.Latin1.GetBytes(key);
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return key;
        }
    }
}
