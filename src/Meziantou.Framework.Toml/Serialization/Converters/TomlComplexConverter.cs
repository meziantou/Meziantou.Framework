using System.Numerics;

namespace Meziantou.Framework.Toml.Serialization.Converters;

// A complex number is an inline table with the keys Real and Imaginary (converted by the property naming policy). The
// parts are read and written by the metadata of their type, so a converter registered for the part type applies.
internal static class TomlComplexConverter
{
#if NET11_0_OR_GREATER
    // The parts of a Complex<T> that TOML supports; the source generator matches the same types. Other parts, such as
    // NFloat, have no TOML representation, so the type is handled like any other struct.
    internal static bool IsSupportedPartType(Type type) =>
        type == typeof(double) || type == typeof(float) || type == typeof(Half) || type == typeof(BFloat16) ||
        type == typeof(Decimal32) || type == typeof(Decimal64) || type == typeof(Decimal128);
#endif

    internal static (TPart Real, TPart Imaginary) Read<TPart>(TomlReader reader, TomlTypeInfo partTypeInfo, Type complexType)
        where TPart : struct
    {
        if (reader.TokenType != TomlTokenType.StartTable)
        {
            throw reader.CreateException($"Expected {TomlTokenType.StartTable} token but was {reader.TokenType}.");
        }

        var options = reader.Options;
        var realName = ConvertName(options, nameof(Complex.Real));
        var imaginaryName = ConvertName(options, nameof(Complex.Imaginary));
        var comparison = options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        TPart real = default;
        TPart imaginary = default;
        reader.Read();
        while (reader.TokenType != TomlTokenType.EndTable)
        {
            if (reader.TokenType != TomlTokenType.PropertyName)
            {
                throw reader.CreateException($"Expected {TomlTokenType.PropertyName} token but was {reader.TokenType}.");
            }

            var name = reader.PropertyName;
            reader.Read();
            if (string.Equals(name, realName, comparison))
            {
                real = ReadPart<TPart>(reader, partTypeInfo);
            }
            else if (string.Equals(name, imaginaryName, comparison))
            {
                imaginary = ReadPart<TPart>(reader, partTypeInfo);
            }
            else
            {
                if (options.UnmappedMemberHandling == TomlUnmappedMemberHandling.Disallow)
                {
                    reader.OperationState.RecordOrThrow(reader.CreateException($"The TOML key '{name}' could not be mapped to '{complexType.FullName}'."));
                }

                reader.Skip();
            }
        }

        reader.Read();
        return (real, imaginary);
    }

    internal static void Write<TPart>(TomlWriter writer, TPart real, TPart imaginary, TomlTypeInfo partTypeInfo)
    {
        var options = writer.Options;
        writer.WriteStartInlineTable();
        writer.WritePropertyName(ConvertName(options, nameof(Complex.Real)));
        WritePart(writer, real, partTypeInfo);
        writer.WritePropertyName(ConvertName(options, nameof(Complex.Imaginary)));
        WritePart(writer, imaginary, partTypeInfo);
        writer.WriteEndInlineTable();
    }

    private static TPart ReadPart<TPart>(TomlReader reader, TomlTypeInfo partTypeInfo)
        where TPart : struct
    {
        var value = partTypeInfo is TomlTypeInfo<TPart> typed ? typed.Read(reader) : partTypeInfo.ReadAsObject(reader);
        return value is TPart part ? part : throw reader.CreateException($"The TOML value cannot be converted to '{typeof(TPart).FullName}'.");
    }

    private static void WritePart<TPart>(TomlWriter writer, TPart value, TomlTypeInfo partTypeInfo)
    {
        if (partTypeInfo is TomlTypeInfo<TPart> typed)
        {
            typed.Write(writer, value);
        }
        else
        {
            partTypeInfo.Write(writer, value);
        }
    }

    private static string ConvertName(TomlSerializerOptions options, string name) => options.PropertyNamingPolicy?.ConvertName(name) ?? name;
}
