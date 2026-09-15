using System.Reflection;

namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlEnumConverter<TEnum> : YamlConverter<TEnum>, IYamlEnumNameFormatter where TEnum : struct, Enum
{
    private readonly Dictionary<TEnum, string>? _valueToName;
    private readonly Dictionary<string, TEnum>? _nameToValue;

    public YamlEnumConverter()
    {
        BuildCustomNameMaps(out _valueToName, out _nameToValue);
    }

    public override TEnum Read(YamlReader reader)
    {
        if (reader.TokenType != YamlTokenType.Scalar)
        {
            throw YamlThrowHelper.ThrowExpectedScalar(reader);
        }

        var text = reader.ScalarValue ?? string.Empty;

        if (_nameToValue is not null && _nameToValue.TryGetValue(text, out var mapped))
        {
            reader.Read();
            return mapped;
        }

        if (Enum.TryParse<TEnum>(text, ignoreCase: true, out var parsed))
        {
            reader.Read();
            return parsed;
        }

        if (YamlScalar.TryParseInt64(reader, out var numeric))
        {
            reader.Read();
            return (TEnum)Enum.ToObject(typeof(TEnum), numeric);
        }

        throw YamlThrowHelper.ThrowInvalidEnumScalar(reader, text);
    }

    public string FormatName(object value)
    {
        var enumValue = (TEnum)value;
        if (_valueToName is not null && _valueToName.TryGetValue(enumValue, out var name))
        {
            return name;
        }

        return FormatDefaultName(enumValue);
    }

    public override void Write(YamlWriter writer, TEnum value)
    {
        if (_valueToName is not null && _valueToName.TryGetValue(value, out var name))
        {
            writer.WriteString(name);
            return;
        }

        writer.WriteScalar(FormatDefaultName(value));
    }

    private static string FormatDefaultName(TEnum value)
    {
        var name = value.ToString();
        if (name.Length == 0 || char.IsAsciiDigit(name[0]) || char.IsLetter(name[0]) || name[0] == '_' || char.GetUnicodeCategory(name[0]) == UnicodeCategory.LetterNumber)
        {
            return name;
        }

        // A value without a name is formatted as a number, and the negative sign of the current culture is not
        // always '-' (ar-SA prefixes it with a directional mark), so the number is formatted with the invariant culture.
        return Type.GetTypeCode(typeof(TEnum)) is TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64
            ? Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)
            : Convert.ToUInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2090",
        Justification = "Enum fields and their custom attributes are preserved for enum types used in serialization.")]
    private static void BuildCustomNameMaps(out Dictionary<TEnum, string>? valueToName, out Dictionary<string, TEnum>? nameToValue)
    {
        valueToName = null;
        nameToValue = null;

        var fields = typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static);
        for (var i = 0; i < fields.Length; i++)
        {
            var field = fields[i];
            var customName = GetCustomName(field);
            if (customName is null)
            {
                continue;
            }

            var value = (TEnum)field.GetValue(null)!;
            valueToName ??= new Dictionary<TEnum, string>();
            nameToValue ??= new Dictionary<string, TEnum>(StringComparer.OrdinalIgnoreCase);

            valueToName[value] = customName;
            nameToValue[customName] = value;
        }
    }

    private static string? GetCustomName(FieldInfo field)
    {
        return field.GetCustomAttribute<YamlEnumMemberNameAttribute>(inherit: false)?.Name;
    }
}
