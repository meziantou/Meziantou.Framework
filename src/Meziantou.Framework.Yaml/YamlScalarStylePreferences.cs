namespace Meziantou.Framework.Yaml;

/// <summary>Provides high-level preferences for scalar style emission.</summary>
public sealed class YamlScalarStylePreferences
{
    /// <summary>Gets or sets a value indicating whether plain style should be preferred when possible.</summary>
    public bool PreferPlainStyle { get; init; } = true;

    /// <summary>Gets or sets a value indicating whether quoted style should be preferred for ambiguous scalars.</summary>
    public bool PreferQuotedForAmbiguousScalars { get; init; } = true;
}

