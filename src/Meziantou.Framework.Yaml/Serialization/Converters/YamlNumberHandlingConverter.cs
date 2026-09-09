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

        var literal = text switch
        {
            "NaN" => NamedFloatLiteral.NaN,
            "Infinity" or "+Infinity" => NamedFloatLiteral.PositiveInfinity,
            "-Infinity" => NamedFloatLiteral.NegativeInfinity,
            _ => NamedFloatLiteral.None,
        };

        if (literal is NamedFloatLiteral.None)
        {
            return false;
        }

        if (_underlyingType == typeof(double))
        {
            value = CreateNamedFloat<double>(literal);
            return true;
        }

        if (_underlyingType == typeof(float))
        {
            value = CreateNamedFloat<float>(literal);
            return true;
        }

#if NET11_0_OR_GREATER
        if (_underlyingType == typeof(BFloat16))
        {
            value = CreateNamedFloat<BFloat16>(literal);
            return true;
        }

        if (_underlyingType == typeof(Decimal32))
        {
            value = CreateNamedFloat<Decimal32>(literal);
            return true;
        }

        if (_underlyingType == typeof(Decimal64))
        {
            value = CreateNamedFloat<Decimal64>(literal);
            return true;
        }

        if (_underlyingType == typeof(Decimal128))
        {
            value = CreateNamedFloat<Decimal128>(literal);
            return true;
        }
#endif

        return false;
    }

    private static object CreateNamedFloat<T>(NamedFloatLiteral literal)
        where T : struct, IFloatingPointIeee754<T>
        => literal switch
        {
            NamedFloatLiteral.NaN => T.NaN,
            NamedFloatLiteral.PositiveInfinity => T.PositiveInfinity,
            _ => T.NegativeInfinity,
        };

    private static bool TryGetNamedFloatLiteral(object value, out string literal)
    {
        switch (value)
        {
            case double doubleValue:
                return TryGetNamedFloatLiteral(doubleValue, out literal);
            case float floatValue:
                return TryGetNamedFloatLiteral(floatValue, out literal);
#if NET11_0_OR_GREATER
            case BFloat16 bfloat16Value:
                return TryGetNamedFloatLiteral(bfloat16Value, out literal);
            case Decimal32 decimal32Value:
                return TryGetNamedFloatLiteral(decimal32Value, out literal);
            case Decimal64 decimal64Value:
                return TryGetNamedFloatLiteral(decimal64Value, out literal);
            case Decimal128 decimal128Value:
                return TryGetNamedFloatLiteral(decimal128Value, out literal);
#endif
            default:
                literal = string.Empty;
                return false;
        }
    }

    private static bool TryGetNamedFloatLiteral<T>(T value, out string literal)
        where T : struct, IFloatingPointIeee754<T>
    {
        if (T.IsNaN(value))
        {
            literal = "NaN";
            return true;
        }

        if (T.IsPositiveInfinity(value))
        {
            literal = "Infinity";
            return true;
        }

        if (T.IsNegativeInfinity(value))
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

    private enum NamedFloatLiteral
    {
        None,
        NaN,
        PositiveInfinity,
        NegativeInfinity,
    }
}
