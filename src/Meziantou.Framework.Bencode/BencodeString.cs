using System.Text.Unicode;

namespace Meziantou.Framework.Bencode;

public sealed class BencodeString : BencodeValue, IEquatable<BencodeString>
{
    private static readonly Encoding Utf8Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private const int MaxDiagnosticByteCount = 32;

    public BencodeString(ReadOnlyMemory<byte> value)
    {
        Value = value;
    }

    public override BencodeValueKind Kind => BencodeValueKind.String;

    public ReadOnlyMemory<byte> Value { get; }

    /// <summary>Decodes the value as UTF-8.</summary>
    /// <exception cref="DecoderFallbackException">The value is not valid UTF-8. Bencode strings hold arbitrary bytes, so use <see cref="ToString"/> when the value may be binary.</exception>
    public string ToUtf8String()
    {
        return Utf8Encoding.GetString(Value.Span);
    }

    public bool Equals([NotNullWhen(true)] BencodeString? other)
    {
        if (other is null)
            return false;

        return Value.Span.SequenceEqual(other.Value.Span);
    }

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is BencodeString other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(Value.Span);
        return hash.ToHashCode();
    }

    public override void WriteTo(BencodeWriter writer, bool canonical)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteString(Value.Span);
    }

    /// <summary>Returns a representation suitable for diagnostics. Bencode strings hold arbitrary bytes, so a value that is not valid UTF-8 is rendered as hexadecimal instead.</summary>
    public override string ToString()
    {
        var span = Value.Span;
        if (Utf8.IsValid(span))
            return Utf8Encoding.GetString(span);

        return span.Length <= MaxDiagnosticByteCount
            ? "0x" + Convert.ToHexString(span)
            : $"0x{Convert.ToHexString(span[..MaxDiagnosticByteCount])}... ({span.Length} bytes)";
    }
}
