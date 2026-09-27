using System;
using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Toml;
using Meziantou.Framework.Toml.Helpers;

namespace Meziantou.Framework.Toml.Serialization;

/// <summary>
/// Overrides inline table formatting for a member.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class TomlInlineTableAttribute : TomlAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TomlInlineTableAttribute"/> class.
    /// </summary>
    /// <param name="policy">The inline table policy.</param>
    public TomlInlineTableAttribute(TomlInlineTablePolicy policy)
    {
        if (policy is not TomlInlineTablePolicy.Never and not TomlInlineTablePolicy.WhenSmall and not TomlInlineTablePolicy.Always)
        {
            throw new ArgumentOutOfRangeException(nameof(policy), policy, "Invalid TOML inline table policy.");
        }

        Policy = policy;
    }

    /// <summary>
    /// Gets the inline table policy.
    /// </summary>
    public TomlInlineTablePolicy Policy { get; }
}
