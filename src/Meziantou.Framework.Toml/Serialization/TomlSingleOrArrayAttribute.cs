using System;
using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Toml;
using Meziantou.Framework.Toml.Helpers;

namespace Meziantou.Framework.Toml.Serialization;

/// <summary>
/// Allows a collection member to be deserialized from either a single TOML value or a TOML array.
/// </summary>
/// <remarks>
/// When the TOML input contains a single value instead of an array, the serializer treats it as a collection containing
/// exactly one element. For mutable read-only collection members, values are appended to the existing collection.
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class TomlSingleOrArrayAttribute : TomlAttribute
{
}
