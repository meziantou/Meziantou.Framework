#if NET11_0_OR_GREATER
namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlDecimal128Converter : YamlIeee754Converter<Decimal128>
{
    public static YamlDecimal128Converter Instance { get; } = new();

    public override void Write(YamlWriter writer, Decimal128 value)
    {
        writer.WriteScalar(value);
    }

    protected override YamlException CreateInvalidScalarException(YamlReader reader)
        => YamlThrowHelper.ThrowInvalidDecimal128Scalar(reader);
}
#endif
