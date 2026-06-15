namespace Meziantou.Framework.Yamlish;

public abstract class YamlishConverterFactory : YamlishConverter
{
    public abstract YamlishConverter? CreateConverter(Type typeToConvert, YamlishSerializerOptions options);

    public sealed override object? Read(YamlishNode node, Type typeToConvert, YamlishSerializerOptions options) => throw new InvalidOperationException();

    public sealed override YamlishNode Write(object value, Type typeToConvert, YamlishSerializerOptions options) => throw new InvalidOperationException();
}
