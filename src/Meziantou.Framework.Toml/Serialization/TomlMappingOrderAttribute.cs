using System;
using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Toml;
using Meziantou.Framework.Toml.Helpers;

namespace Meziantou.Framework.Toml.Serialization;

/// <summary>
/// Overrides member ordering for a TOML-serializable type.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = true)]
public sealed class TomlMappingOrderAttribute : TomlAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TomlMappingOrderAttribute"/> class.
    /// </summary>
    /// <param name="policy">The mapping order policy.</param>
    public TomlMappingOrderAttribute(TomlMappingOrderPolicy policy)
    {
        if (policy is not TomlMappingOrderPolicy.Declaration and not TomlMappingOrderPolicy.Alphabetical and not TomlMappingOrderPolicy.OrderThenDeclaration and not TomlMappingOrderPolicy.OrderThenAlphabetical)
        {
            throw new ArgumentOutOfRangeException(nameof(policy), policy, "Invalid TOML mapping order policy.");
        }

        Policy = policy;
    }

    /// <summary>
    /// Gets the mapping order policy.
    /// </summary>
    public TomlMappingOrderPolicy Policy { get; }
}
