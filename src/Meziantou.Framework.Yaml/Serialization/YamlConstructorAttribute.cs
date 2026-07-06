namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>Specifies which constructor should be used during YAML deserialization.</summary>
[AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false)]
public sealed class YamlConstructorAttribute : YamlAttribute
{
}

