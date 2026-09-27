namespace Meziantou.Framework.Toml;

/// <summary>Determines how deserialization handles an existing instance of a property or field.</summary>
public enum TomlObjectCreationHandling
{
    /// <summary>A new instance is always created and assigned to the member.</summary>
    Replace = 0,

    /// <summary>The existing instance is populated when possible instead of being replaced.</summary>
    Populate = 1,
}
