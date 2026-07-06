namespace Meziantou.Framework.Yaml;

/// <summary>Controls how polymorphic type discriminators are encoded.</summary>
public enum YamlTypeDiscriminatorStyle
{
    /// <summary>Use the serializer options default.</summary>
    Unspecified = -1,

    /// <summary>Use YAML tags only.</summary>
    Tag = 0,

    /// <summary>Use discriminator property only.</summary>
    Property = 1,

    /// <summary>Accept or emit both tag and discriminator property.</summary>
    Both = 2,
}
