using System.Text;

namespace Meziantou.Framework.Bencode;

public sealed class BencodeString : BencodeValue
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
}
