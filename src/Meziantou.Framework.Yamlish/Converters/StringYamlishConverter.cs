namespace Meziantou.Framework.Yamlish;

internal sealed class StringYamlishConverter : YamlishConverter<string>
{
    public override string Read(YamlishNode node, YamlishSerializerOptions options)
    {
        return ConverterUtilities.GetScalarValue(node, typeof(string));
    }

    public override YamlishNode Write(string value, YamlishSerializerOptions options)
    {
        return new YamlishScalar(value);
    }
}
