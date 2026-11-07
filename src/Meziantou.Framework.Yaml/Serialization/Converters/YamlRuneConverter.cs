using System.Text;

namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlRuneConverter : YamlConverter<Rune>
{
    public static YamlRuneConverter Instance { get; } = new();

    public override Rune Read(YamlReader reader)
    {
        if (reader.TokenType != YamlTokenType.Scalar)
        {
            throw YamlThrowHelper.ThrowExpectedScalar(reader);
        }

        var text = reader.ScalarValue ?? string.Empty;
        if (text.Length == 0 || !Rune.TryGetRuneAt(text, 0, out var result) || result.Utf16SequenceLength != text.Length)
        {
            throw YamlThrowHelper.ThrowInvalidRuneScalar(reader, text);
        }

        reader.Read();
        return result;
    }

    public override void Write(YamlWriter writer, Rune value)
    {
        writer.WriteScalar(value.ToString());
    }
}
