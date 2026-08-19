#if NET11_0_OR_GREATER
namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlBFloat16Converter : YamlIeee754Converter<BFloat16>
{
    public static YamlBFloat16Converter Instance { get; } = new();

    public override void Write(YamlWriter writer, BFloat16 value)
    {
        writer.WriteScalar(value);
    }

    protected override YamlException CreateInvalidScalarException(YamlReader reader)
        => YamlThrowHelper.ThrowInvalidBFloat16Scalar(reader);
}
#endif
