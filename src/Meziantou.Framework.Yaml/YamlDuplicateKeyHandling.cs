namespace Meziantou.Framework.Yaml;

/// <summary>Defines how duplicate mapping keys are handled when reading YAML.</summary>
public enum YamlDuplicateKeyHandling
{
    /// <summary>Throw an exception when a duplicate key is found.</summary>
    Error = 0,

    /// <summary>Keep the last value for a duplicate key.</summary>
    LastWins = 1,

    /// <summary>Keep the first value for a duplicate key.</summary>
    FirstWins = 2,
}

