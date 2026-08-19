using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml;

/// <summary>Adapts a non-generic <see cref="YamlTypeInfo"/> to the strongly typed <see cref="YamlTypeInfo{T}"/> contract.</summary>
internal sealed class DelegatingYamlTypeInfo<T> : YamlTypeInfo<T>
{
    private readonly YamlTypeInfo _typeInfo;

    public DelegatingYamlTypeInfo(YamlTypeInfo typeInfo) : base(typeInfo.Options)
    {
        _typeInfo = typeInfo;
    }

    public override void Write(YamlWriter writer, T value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _typeInfo.Write(writer, value);
    }

    public override T? Read(YamlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var value = _typeInfo.ReadAsObject(reader);
        return value is null ? default : (T)value;
    }
}
