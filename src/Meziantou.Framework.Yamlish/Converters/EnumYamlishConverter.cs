namespace Meziantou.Framework.Yamlish.Converters;

internal sealed class EnumYamlishConverter : YamlishConverter
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override object Read(YamlishNode node, Type typeToConvert, YamlishSerializerOptions options)
    {
        return Enum.Parse(typeToConvert, ConverterUtilities.GetScalarValue(node, typeToConvert), ignoreCase: true);
    }

    public override YamlishNode Write(object value, Type typeToConvert, YamlishSerializerOptions options)
    {
        return new YamlishScalar(value.ToString() ?? string.Empty);
    }
}
