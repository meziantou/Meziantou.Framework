namespace Meziantou.Framework.Yamlish;

internal sealed class BooleanYamlishConverter : YamlishConverter<bool>
{
    public override bool Read(YamlishNode node, YamlishSerializerOptions options)
    {
        return bool.Parse(ConverterUtilities.GetScalarValue(node, typeof(bool)));
    }

    public override YamlishNode Write(bool value, YamlishSerializerOptions options)
    {
        return new YamlishScalar(value ? "true" : "false");
    }
}
