namespace Meziantou.Framework.Bencode;

public sealed class BencodeString : BencodeValue, IEquatable<BencodeString>
{
    private static readonly Encoding Utf8Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public BencodeString(ReadOnlyMemory<byte> value)
    {
        Value = value;
    }

    public override BencodeValueKind Kind => BencodeValueKind.String;

    public ReadOnlyMemory<byte> Value { get; }

    public string ToUtf8String()
    {
        return Utf8Encoding.GetString(Value.Span);
    }

    public bool Equals(BencodeString? other)
    {
        if (other is null)
            return false;

        return Value.Span.SequenceEqual(other.Value.Span);
    }

    public override bool Equals(object? obj) => obj is BencodeString other && Equals(other);

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

    public override string ToString() => ToUtf8String();
}
