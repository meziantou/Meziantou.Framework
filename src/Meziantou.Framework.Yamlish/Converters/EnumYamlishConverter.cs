namespace Meziantou.Framework.Yamlish;

internal sealed class EnumYamlishConverter(Type enumType) : YamlishConverter
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert == enumType;

    public override object Read(YamlishNode node, Type typeToConvert, YamlishSerializerOptions options)
    {
        return Enum.Parse(enumType, ConverterUtilities.GetScalarValue(node, enumType), ignoreCase: true);
    }

    public override YamlishNode Write(object value, Type typeToConvert, YamlishSerializerOptions options)
    {
        return new YamlishScalar(value.ToString() ?? string.Empty);
    }
}
