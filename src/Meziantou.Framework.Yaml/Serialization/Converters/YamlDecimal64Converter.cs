#if NET11_0_OR_GREATER
namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlDecimal64Converter : YamlIeee754Converter<Decimal64>
{
    public static YamlDecimal64Converter Instance { get; } = new();

    public override void Write(YamlWriter writer, Decimal64 value)
    {
        writer.WriteScalar(value);
    }

    protected override YamlException CreateInvalidScalarException(YamlReader reader)
        => YamlThrowHelper.ThrowInvalidDecimal64Scalar(reader);
}
#endif
