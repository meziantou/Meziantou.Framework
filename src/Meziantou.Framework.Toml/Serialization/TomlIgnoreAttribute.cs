using System;
using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Toml;
using Meziantou.Framework.Toml.Helpers;

namespace Meziantou.Framework.Toml.Serialization;

/// <summary>
/// Instructs the <see cref="Meziantou.Framework.Toml.TomlSerializer"/> when to ignore the field or property value.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
public sealed class TomlIgnoreAttribute : TomlAttribute
{
    /// <summary>
    /// Gets or sets the condition that must be met before the member is ignored.
    /// </summary>
    /// <remarks>The default value is <see cref="TomlIgnoreCondition.Always"/>.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">Value is not a defined <see cref="TomlIgnoreCondition"/>.</exception>
    public TomlIgnoreCondition Condition
    {
        get;
        set => field = ArgumentGuard.ThrowIfNotDefined(value, nameof(value));
    } = TomlIgnoreCondition.Always;
}
