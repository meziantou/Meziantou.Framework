namespace Meziantou.Framework.Yamlish;

public abstract class YamlishConverter
{
    public abstract bool CanConvert(Type typeToConvert);

    public abstract object? Read(YamlishNode node, Type typeToConvert, YamlishSerializerOptions options);

    public abstract YamlishNode Write(object value, Type typeToConvert, YamlishSerializerOptions options);
}
