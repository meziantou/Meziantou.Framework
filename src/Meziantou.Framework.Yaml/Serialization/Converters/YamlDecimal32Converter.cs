#if NET11_0_OR_GREATER
namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlDecimal32Converter : YamlIeee754Converter<Decimal32>
{
    public static YamlDecimal32Converter Instance { get; } = new();

    public override void Write(YamlWriter writer, Decimal32 value)
    {
        writer.WriteScalar(value);
    }

    protected override YamlException CreateInvalidScalarException(YamlReader reader)
        => YamlThrowHelper.ThrowInvalidDecimal32Scalar(reader);
}
#endif
