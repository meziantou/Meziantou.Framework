using System;
using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Toml;

namespace Meziantou.Framework.Toml.Serialization;

/// <summary>
/// Overrides string formatting for a string member.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class TomlStringStyleAttribute : TomlAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TomlStringStyleAttribute"/> class.
    /// </summary>
    /// <param name="style">The preferred string style.</param>
    public TomlStringStyleAttribute(TomlStringStyle style)
    {
        if (style is not TomlStringStyle.Basic and not TomlStringStyle.Literal and not TomlStringStyle.MultilineBasic and not TomlStringStyle.MultilineLiteral)
        {
            throw new ArgumentOutOfRangeException(nameof(style), style, "Invalid TOML string style.");
        }

        Style = style;
    }

    /// <summary>
    /// Gets the preferred string style.
    /// </summary>
    public TomlStringStyle Style { get; }

    /// <summary>
    /// Gets or sets whether literal strings should be preferred when no escaping is required.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is not a defined <see cref="TomlBooleanPreference"/>.</exception>
    public TomlBooleanPreference PreferLiteralWhenNoEscapes
    {
        get;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, $"The value is not a defined {nameof(TomlBooleanPreference)}.");
            }

            field = value;
        }
    }

    /// <summary>
    /// Gets or sets whether control characters up to U+00FF are escaped with the TOML 1.1 <c>\xHH</c> and <c>\e</c> escapes
    /// instead of <c>\uXXXX</c>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is not a defined <see cref="TomlBooleanPreference"/>.</exception>
    public TomlBooleanPreference AllowHexEscapes
    {
        get;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, $"The value is not a defined {nameof(TomlBooleanPreference)}.");
            }

            field = value;
        }
    }
}
