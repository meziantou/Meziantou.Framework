using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml;

/// <summary>
/// Provides reflection-based type metadata for <see cref="YamlSerializer"/>.
/// </summary>
public sealed class ReflectionYamlTypeInfoResolver : IYamlTypeInfoResolver
{
    private sealed class ReflectionYamlTypeInfo : YamlTypeInfo
    {
        public ReflectionYamlTypeInfo(Type type, YamlSerializerOptions options) : base(type, options)
        {
        }

        public override void Write(YamlWriter writer, object? value)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.GetConverter(Type).Write(writer, value);
        }

        public override object? ReadAsObject(YamlReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader);
            return reader.GetConverter(Type).Read(reader, Type);
        }
    }

    /// <summary>Gets a shared default reflection resolver instance.</summary>
    public static ReflectionYamlTypeInfoResolver Default { get; } = new();

    /// <inheritdoc />
    public YamlTypeInfo? GetTypeInfo(Type type, YamlSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(options);
        return new ReflectionYamlTypeInfo(type, options);
    }
}
