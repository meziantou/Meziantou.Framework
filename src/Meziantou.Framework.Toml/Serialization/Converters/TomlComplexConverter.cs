using System.Numerics;
using Meziantou.Framework.Toml.Serialization.Internal;

namespace Meziantou.Framework.Toml.Serialization.Converters;

// A complex number is an inline table with the keys Real and Imaginary (converted by the property naming policy)
internal sealed class TomlComplexConverter : TomlConverter<Complex>
{
    public static TomlComplexConverter Instance { get; } = new();

    public override Complex Read(TomlReader reader)
    {
        var (real, imaginary) = Read(reader, TomlDoubleConverter.Instance, typeof(Complex));
        return new Complex(real, imaginary);
    }

    public override void Write(TomlWriter writer, Complex value) => Write(writer, value.Real, value.Imaginary, TomlDoubleConverter.Instance);

#if NET11_0_OR_GREATER
    // The converter of the parts of a Complex<T>, or null when T is not a floating-point type that TOML supports
    internal static TomlConverter<T>? GetPartConverter<T>()
    {
        if (typeof(T) == typeof(double)) return (TomlConverter<T>)(object)TomlDoubleConverter.Instance;
        if (typeof(T) == typeof(float)) return (TomlConverter<T>)(object)TomlSingleConverter.Instance;
        if (typeof(T) == typeof(Half)) return (TomlConverter<T>)(object)TomlHalfConverter.Instance;
        if (typeof(T) == typeof(BFloat16)) return (TomlConverter<T>)(object)TomlBFloat16Converter.Instance;
        if (typeof(T) == typeof(Decimal32)) return (TomlConverter<T>)(object)TomlDecimalFloatingPointConverter.Decimal32;
        if (typeof(T) == typeof(Decimal64)) return (TomlConverter<T>)(object)TomlDecimalFloatingPointConverter.Decimal64;
        if (typeof(T) == typeof(Decimal128)) return (TomlConverter<T>)(object)TomlDecimalFloatingPointConverter.Decimal128;
        return null;
    }
#endif

    internal static (TPart Real, TPart Imaginary) Read<TPart>(TomlReader reader, TomlConverter<TPart> partConverter, Type complexType)
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

            var name = reader.PropertyName!;
            reader.Read();
            if (string.Equals(name, realName, comparison))
            {
                real = TomlConverterHelper.Read(reader, partConverter);
            }
            else if (string.Equals(name, imaginaryName, comparison))
            {
                imaginary = TomlConverterHelper.Read(reader, partConverter);
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

    internal static void Write<TPart>(TomlWriter writer, TPart real, TPart imaginary, TomlConverter<TPart> partConverter)
    {
        var options = writer.Options;
        writer.WriteStartInlineTable();
        writer.WritePropertyName(ConvertName(options, nameof(Complex.Real)));
        partConverter.Write(writer, real);
        writer.WritePropertyName(ConvertName(options, nameof(Complex.Imaginary)));
        partConverter.Write(writer, imaginary);
        writer.WriteEndInlineTable();
    }

    private static string ConvertName(TomlSerializerOptions options, string name) => options.PropertyNamingPolicy?.ConvertName(name) ?? name;
}

#if NET11_0_OR_GREATER
internal sealed class TomlComplexConverter<T> : TomlConverter<Complex<T>>
    where T : struct, IFloatingPointIeee754<T>, IMinMaxValue<T>
{
    private readonly TomlConverter<T> _partConverter;

    public TomlComplexConverter(TomlConverter<T> partConverter)
    {
        _partConverter = partConverter;
    }

    public override Complex<T> Read(TomlReader reader)
    {
        var (real, imaginary) = TomlComplexConverter.Read(reader, _partConverter, typeof(Complex<T>));
        return new Complex<T>(real, imaginary);
    }

    public override void Write(TomlWriter writer, Complex<T> value) => TomlComplexConverter.Write(writer, value.Real, value.Imaginary, _partConverter);
}
#endif
