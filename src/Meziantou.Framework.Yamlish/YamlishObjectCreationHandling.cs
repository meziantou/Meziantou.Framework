namespace Meziantou.Framework.Yamlish;

/// <summary>Specifies how existing object instances are handled during deserialization.</summary>
public enum YamlishObjectCreationHandling
{
    /// <summary>Creates a new object instance instead of reusing an existing value.</summary>
    Replace,

    /// <summary>Populates an existing object instance when possible.</summary>
    Populate,
}
