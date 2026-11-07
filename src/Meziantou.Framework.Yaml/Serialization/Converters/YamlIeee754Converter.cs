#if NET11_0_OR_GREATER
namespace Meziantou.Framework.Yaml.Serialization.Converters;

/// <summary>
/// Base converter for IEEE 754 floating-point types that share the same YAML scalar representation,
/// including the <c>.inf</c>, <c>-.inf</c>, and <c>.nan</c> literals of the YAML core schema.
/// </summary>
internal abstract class YamlIeee754Converter<T> : YamlConverter<T>
    where T : struct, IFloatingPointIeee754<T>
{
    public override T Read(YamlReader reader)
    {
        if (reader.TokenType != YamlTokenType.Scalar)
        {
            throw YamlThrowHelper.ThrowExpectedScalar(reader);
        }

        if (!YamlScalar.TryParseIeee754<T>(reader, out var result))
        {
            throw CreateInvalidScalarException(reader);
        }

        reader.Read();
        return result;
    }

    protected abstract YamlException CreateInvalidScalarException(YamlReader reader);
}
#endif
