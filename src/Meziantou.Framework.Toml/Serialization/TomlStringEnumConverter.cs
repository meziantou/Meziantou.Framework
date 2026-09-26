using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Serialization.Converters;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml.Serialization;

/// <summary>
/// Converts enum values to and from their names, instead of their numeric values.
/// </summary>
/// <remarks>
/// <para>
/// Apply it with <see cref="TomlConverterAttribute"/> to an enum type or to a member, or add it to
/// <see cref="TomlSerializerOptions.Converters"/> to write every enum as a string. <see cref="TomlStringEnumMemberNameAttribute"/>
/// sets the name of an enum value. A flags value is written as a comma-separated list of names.
/// </para>
/// <para>
/// Names are read case-insensitively, and integers are read as numeric values.
/// </para>
/// </remarks>
public sealed class TomlStringEnumConverter : TomlConverter
{
    // A weak table, so that the enums of a collectible assembly can be unloaded
    private static readonly ConditionalWeakTable<Type, EnumMemberNames> MemberNamesCache = new();

    internal static TomlStringEnumConverter Instance { get; } = new();

    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        return typeToConvert.IsEnum;
    }

    /// <inheritdoc />
    public override object? Read(TomlReader reader, Type typeToConvert)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(typeToConvert);
        if (reader.TokenType == TomlTokenType.String && typeToConvert.IsEnum)
        {
            var name = reader.GetString();
            if (!Enum.TryParse(typeToConvert, GetMemberNames(typeToConvert).ToEnumNames(name), ignoreCase: false, out var parsed))
            {
                throw reader.CreateException($"Invalid enum name `{name.ToPrintableInputText()}` for type '{typeToConvert.FullName}'.");
            }

            reader.Read();
            return parsed;
        }

        return TomlEnumConverter.Instance.Read(reader, typeToConvert);
    }

    /// <inheritdoc />
    public override void Write(TomlWriter writer, object? value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (value is null)
        {
            throw new TomlException("TOML does not support null values.");
        }

        var type = value.GetType();
        if (!type.IsEnum)
        {
            throw new TomlException($"Expected an enum value but was '{type.FullName}'.");
        }

        writer.WriteStringValue(GetMemberNames(type).ToCustomNames(value.ToString()!));
    }

    // The names set with [TomlStringEnumMemberName]
    private static EnumMemberNames GetMemberNames(Type enumType) => MemberNamesCache.GetValue(enumType, static type => EnumMemberNames.Create(type));

    private sealed class EnumMemberNames
    {
        // The name written for each member that has a custom name
        private readonly Dictionary<string, string>? _toCustom;

        // The member of each written name. The exact name wins, so members that differ only by case round-trip; any casing
        // is accepted otherwise.
        private readonly Dictionary<string, string> _exact;
        private readonly Dictionary<string, string> _ignoreCase;

        private EnumMemberNames(Dictionary<string, string>? toCustom, Dictionary<string, string> exact, Dictionary<string, string> ignoreCase)
        {
            _toCustom = toCustom;
            _exact = exact;
            _ignoreCase = ignoreCase;
        }

        [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "The trimmer keeps every field of an enum type.")]
        public static EnumMemberNames Create(Type type)
        {
            Dictionary<string, string>? toCustom = null;
            var exact = new Dictionary<string, string>(StringComparer.Ordinal);
            var ignoreCase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var customName = field.GetCustomAttribute<TomlStringEnumMemberNameAttribute>()?.Name;
                if (customName is not null)
                {
                    // A flags value is a comma-separated list of names, which are trimmed when read
                    if (customName.Contains(',', StringComparison.Ordinal) || customName.Trim().Length != customName.Length)
                    {
                        throw TomlException.CreateConfigurationError($"The enum member name '{customName}' of '{type.FullName}.{field.Name}' cannot contain a comma or start or end with white space.");
                    }

                    (toCustom ??= new(StringComparer.Ordinal))[field.Name] = customName;
                }

                var name = customName ?? field.Name;
                if (!exact.TryAdd(name, field.Name))
                {
                    throw TomlException.CreateConfigurationError($"The enum member name '{name}' is used by several members of '{type.FullName}'.");
                }

                ignoreCase.TryAdd(name, field.Name);
            }

            return new EnumMemberNames(toCustom, exact, ignoreCase);
        }

        // A flags value is written as "A, B"
        public string ToCustomNames(string names)
        {
            if (_toCustom is null)
            {
                return names;
            }

            return Map(names, static (map, name) => map.TryGetValue(name, out var customName) ? customName : name, _toCustom);
        }

        public string ToEnumNames(string names) => Map(names, static (self, name) => self.GetMemberName(name), this);

        private string GetMemberName(string name)
        {
            if (_exact.TryGetValue(name, out var memberName) || _ignoreCase.TryGetValue(name, out memberName))
            {
                return memberName;
            }

            return name;
        }

        private static string Map<TState>(string names, Func<TState, string, string> map, TState state)
        {
            if (!names.Contains(',', StringComparison.Ordinal))
            {
                return map(state, names.Trim());
            }

            var parts = names.Split(',', StringSplitOptions.TrimEntries);
            for (var i = 0; i < parts.Length; i++)
            {
                parts[i] = map(state, parts[i]);
            }

            return string.Join(", ", parts);
        }
    }
}
