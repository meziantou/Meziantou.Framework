using System.Numerics;

namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlBigIntegerConverter : YamlConverter<BigInteger>
{
    public static YamlBigIntegerConverter Instance { get; } = new();

    public override BigInteger Read(YamlReader reader)
    {
        if (reader.TokenType != YamlTokenType.Scalar)
        {
            throw YamlThrowHelper.ThrowExpectedScalar(reader);
        }

        if (!BigInteger.TryParse(reader.ScalarValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            throw YamlThrowHelper.ThrowInvalidBigIntegerScalar(reader);
        }

        reader.Read();
        return result;
    }

    public override void Write(YamlWriter writer, BigInteger value)
    {
        writer.WriteScalar(value);
    }
}
