using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Serialization.Converters;

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
    private static readonly ConcurrentDictionary<Type, EnumMemberNames?> MemberNamesCache = new();

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
        if (reader.TokenType == TomlTokenType.String && typeToConvert.IsEnum && GetMemberNames(typeToConvert) is { } memberNames)
        {
            var name = reader.GetString();
            try
            {
                var parsed = Enum.Parse(typeToConvert, memberNames.ToEnumNames(name), ignoreCase: true);
                reader.Read();
                return parsed;
            }
            catch (Exception ex) when (ex is ArgumentException or OverflowException)
            {
                throw reader.CreateException($"Invalid enum name `{name}` for type '{typeToConvert.FullName}'.");
            }
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

        var text = value.ToString()!;
        writer.WriteStringValue(GetMemberNames(type) is { } memberNames ? memberNames.ToCustomNames(text) : text);
    }

    // The names set with [TomlStringEnumMemberName]
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "The trimmer keeps every field of an enum type.")]
    private static EnumMemberNames? GetMemberNames(Type enumType)
    {
        return MemberNamesCache.GetOrAdd(enumType, static type =>
        {
            Dictionary<string, string>? toCustom = null;
            Dictionary<string, string>? fromCustom = null;
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var name = field.GetCustomAttribute<TomlStringEnumMemberNameAttribute>()?.Name;
                if (name is not null)
                {
                    (toCustom ??= new(StringComparer.Ordinal))[field.Name] = name;
                    (fromCustom ??= new(StringComparer.OrdinalIgnoreCase))[name] = field.Name;
                }
            }

            return toCustom is null ? null : new EnumMemberNames(toCustom, fromCustom!);
        });
    }

    private sealed class EnumMemberNames(Dictionary<string, string> toCustom, Dictionary<string, string> fromCustom)
    {
        // A flags value is written as "A, B"
        public string ToCustomNames(string names) => Map(names, toCustom);

        public string ToEnumNames(string names) => Map(names, fromCustom);

        private static string Map(string names, Dictionary<string, string> map)
        {
            if (map.TryGetValue(names, out var mapped))
            {
                return mapped;
            }

            if (!names.Contains(',', StringComparison.Ordinal))
            {
                return names;
            }

            var parts = names.Split(',', StringSplitOptions.TrimEntries);
            for (var i = 0; i < parts.Length; i++)
            {
                if (map.TryGetValue(parts[i], out var part))
                {
                    parts[i] = part;
                }
            }

            return string.Join(", ", parts);
        }
    }
}
