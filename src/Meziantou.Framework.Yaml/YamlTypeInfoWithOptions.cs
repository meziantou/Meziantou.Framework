using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml;

/// <summary>
/// Wraps a <see cref="YamlTypeInfo"/> to use a different <see cref="YamlSerializerOptions"/>.
/// </summary>
internal sealed class YamlTypeInfoWithOptions : YamlTypeInfo
{
    private readonly YamlTypeInfo _inner;

    internal YamlTypeInfoWithOptions(YamlTypeInfo inner, YamlSerializerOptions options)
        : base(inner.Type, options)
    {
        _inner = inner;
    }

    public override void Write(YamlWriter writer, object? value) => _inner.Write(writer, value);

    public override object? ReadAsObject(YamlReader reader) => _inner.ReadAsObject(reader);
}

