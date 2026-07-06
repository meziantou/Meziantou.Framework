namespace Meziantou.Framework.Yaml.Serialization.Converters;

/// <summary>
/// Wraps a built-in numeric converter to honor <see cref="YamlNumberHandling"/> for a member or type.
/// </summary>
/// <remarks>
/// This type is part of the infrastructure used by the source generator and reflection-based serialization.
/// It is not intended to be used directly.
/// </remarks>
public sealed class YamlNumberHandlingConverter : YamlConverter
{
    private readonly YamlConverter _inner;
    private readonly Type _type;
    private readonly Type _underlyingType;
    private readonly YamlNumberHandling _handling;

    /// <summary>
    /// Initializes a new instance of the <see cref="YamlNumberHandlingConverter"/> class.
    /// </summary>
    /// <param name="inner">The underlying numeric converter to wrap.</param>
    /// <param name="type">The numeric CLR type handled by the converter.</param>
    /// <param name="handling">The number handling to apply.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> or <paramref name="type"/> is <see langword="null"/>.</exception>
    public YamlNumberHandlingConverter(YamlConverter inner, Type type, YamlNumberHandling handling)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(type);
        _inner = inner;
        _type = type;
        _underlyingType = Nullable.GetUnderlyingType(type) ?? type;
        _handling = handling;
    }

    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert) => typeToConvert == _type;

    /// <inheritdoc />
    public override object? Read(YamlReader reader, Type typeToConvert)
    {
        if ((_handling & YamlNumberHandling.AllowNamedFloatingPointLiterals) != YamlNumberHandling.None &&
            reader.TokenType == YamlTokenType.Scalar &&
            TryReadNamedFloat(reader.ScalarValue, out var named))
        {
            reader.Read();
            return named;
        }

        return _inner.Read(reader, typeToConvert);
    }

    /// <inheritdoc />
    public override void Write(YamlWriter writer, object? value)
    {
        if (value is not null)
        {
            if ((_handling & YamlNumberHandling.AllowNamedFloatingPointLiterals) != YamlNumberHandling.None &&
                TryGetNamedFloatLiteral(value, out var literal))
            {
                writer.WriteString(literal);
                return;
            }

            if ((_handling & YamlNumberHandling.WriteAsString) != YamlNumberHandling.None)
            {
                writer.WriteString(FormatInvariant(value));
                return;
            }
        }

        _inner.Write(writer, value);
    }

    private bool TryReadNamedFloat(string? text, out object? value)
    {
        value = null;
        if (_underlyingType != typeof(double) && _underlyingType != typeof(float))
        {
            return false;
        }

        double? parsed = text switch
        {
            "NaN" => double.NaN,
            "Infinity" => double.PositiveInfinity,
            "+Infinity" => double.PositiveInfinity,
            "-Infinity" => double.NegativeInfinity,
            _ => null,
        };

        if (parsed is null)
        {
            return false;
        }

        value = _underlyingType == typeof(float) ? (float)parsed.Value : parsed.Value;
        return true;
    }

    private static bool TryGetNamedFloatLiteral(object value, out string literal)
    {
        double d;
        switch (value)
        {
            case double doubleValue:
                d = doubleValue;
                break;
            case float floatValue:
                d = floatValue;
                break;
            default:
                literal = string.Empty;
                return false;
        }

        if (double.IsNaN(d))
        {
            literal = "NaN";
            return true;
        }

        if (double.IsPositiveInfinity(d))
        {
            literal = "Infinity";
            return true;
        }

        if (double.IsNegativeInfinity(d))
        {
            literal = "-Infinity";
            return true;
        }

        literal = string.Empty;
        return false;
    }

    private static string FormatInvariant(object value)
        => value is IFormattable formattable
            ? formattable.ToString(format: null, CultureInfo.InvariantCulture)
            : value.ToString() ?? string.Empty;
}
