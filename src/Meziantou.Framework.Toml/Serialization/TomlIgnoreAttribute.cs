using System;
using System.Diagnostics.CodeAnalysis;
using Tomlyn;
using Tomlyn.Helpers;

namespace Tomlyn.Serialization;

/// <summary>
/// Instructs the <see cref="Tomlyn.TomlSerializer"/> when to ignore the field or property value.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
public sealed class TomlIgnoreAttribute : TomlAttribute
{
    /// <summary>
    /// Gets or sets the condition that must be met before the member is ignored.
    /// </summary>
    /// <remarks>The default value is <see cref="TomlIgnoreCondition.Always"/>.</remarks>
    public TomlIgnoreCondition Condition { get; set; } = TomlIgnoreCondition.Always;
}
