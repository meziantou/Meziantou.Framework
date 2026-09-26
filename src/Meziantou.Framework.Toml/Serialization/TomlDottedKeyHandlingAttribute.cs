using System;
using System.Diagnostics.CodeAnalysis;
using Tomlyn;
using Tomlyn.Helpers;

namespace Tomlyn.Serialization;

/// <summary>
/// Overrides dotted-key handling for member names declared on a TOML-serializable type.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = true)]
public sealed class TomlDottedKeyHandlingAttribute : TomlAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TomlDottedKeyHandlingAttribute"/> class.
    /// </summary>
    /// <param name="handling">The dotted-key handling behavior.</param>
    public TomlDottedKeyHandlingAttribute(TomlDottedKeyHandling handling)
    {
        if (handling is not TomlDottedKeyHandling.Literal and not TomlDottedKeyHandling.Expand)
        {
            throw new ArgumentOutOfRangeException(nameof(handling), handling, "Invalid TOML dotted key handling.");
        }

        Handling = handling;
    }

    /// <summary>
    /// Gets the dotted-key handling behavior.
    /// </summary>
    public TomlDottedKeyHandling Handling { get; }
}
