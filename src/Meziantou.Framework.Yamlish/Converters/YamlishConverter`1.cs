namespace Meziantou.Framework.Yamlish;

public abstract class YamlishConverter<T> : YamlishConverter
{
    public sealed override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(T);

    public abstract T? Read(YamlishNode node, YamlishSerializerOptions options);

    public abstract YamlishNode Write(T value, YamlishSerializerOptions options);

    public sealed override object? Read(YamlishNode node, Type typeToConvert, YamlishSerializerOptions options)
    {
        return Read(node, options);
    }

    public sealed override YamlishNode Write(object value, Type typeToConvert, YamlishSerializerOptions options)
    {
        return Write((T)value, options);
    }
}
