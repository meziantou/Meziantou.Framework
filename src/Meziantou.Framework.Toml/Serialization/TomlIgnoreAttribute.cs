using System;
using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Toml;

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
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, $"The value is not a defined {nameof(TomlIgnoreCondition)}.");
            }

            field = value;
        }
    } = TomlIgnoreCondition.Always;
}
