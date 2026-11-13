namespace Meziantou.Framework.Yaml.Serialization.Converters;

/// <summary>
/// Converts a complex number as a mapping with a <c>Real</c> and an <c>Imaginary</c> entry, whose names follow
/// <see cref="YamlSerializerOptions.PropertyNamingPolicy"/>.
/// </summary>
/// <remarks>
/// The parts are read and written by the converter of their type, so they can be <c>.nan</c> or <c>.inf</c>. A missing entry
/// is zero and an unknown entry, such as the <c>Magnitude</c> written by older versions, is skipped. The source generator
/// emits the same shape.
/// </remarks>
internal abstract class YamlComplexConverterBase<TComplex, TPart> : YamlConverter<TComplex>
    where TComplex : struct
    where TPart : struct
{
    private YamlConverter? _partConverter;

    protected abstract TComplex Create(TPart real, TPart imaginary);

    protected abstract TPart GetReal(TComplex value);

    protected abstract TPart GetImaginary(TComplex value);

    public override TComplex Read(YamlReader reader)
    {
        if (reader.TryReadAlias(out var rootAliasValue))
        {
            return (TComplex)rootAliasValue!;
        }

        if (reader.TokenType == YamlTokenType.Scalar && YamlScalar.IsNull(reader))
        {
            reader.Read();
            return default;
        }

        if (reader.TokenType != YamlTokenType.StartMapping)
        {
            throw YamlThrowHelper.ThrowExpectedMapping(reader);
        }

        _partConverter ??= reader.GetConverter(typeof(TPart));
        var anchor = reader.Anchor;
        var comparison = reader.Options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var realName = reader.ConvertName("Real");
        var imaginaryName = reader.ConvertName("Imaginary");
        var real = default(TPart);
        var imaginary = default(TPart);
        reader.Read();
        while (reader.TokenType != YamlTokenType.EndMapping)
        {
            if (reader.TokenType != YamlTokenType.Scalar)
            {
                throw YamlThrowHelper.ThrowExpectedScalarKey(reader);
            }

            var name = reader.ScalarValue ?? string.Empty;
            reader.Read();
            if (string.Equals(name, realName, comparison))
            {
                real = (TPart)_partConverter.Read(reader, typeof(TPart))!;
            }
            else if (string.Equals(name, imaginaryName, comparison))
            {
                imaginary = (TPart)_partConverter.Read(reader, typeof(TPart))!;
            }
            else
            {
                reader.Skip();
            }
        }

        reader.Read();
        var result = Create(real, imaginary);
        if (anchor is not null)
        {
            reader.RegisterAnchor(anchor, result);
        }

        return result;
    }

    public override void Write(YamlWriter writer, TComplex value)
    {
        _partConverter ??= writer.GetConverter(typeof(TPart));
        writer.WriteStartMapping();
        writer.WritePropertyName(writer.ConvertName("Real"));
        _partConverter.Write(writer, GetReal(value));
        writer.WritePropertyName(writer.ConvertName("Imaginary"));
        _partConverter.Write(writer, GetImaginary(value));
        writer.WriteEndMapping();
    }
}
