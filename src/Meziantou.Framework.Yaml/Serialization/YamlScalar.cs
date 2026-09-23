using Meziantou.Framework.Yaml.Schemas;
using ScalarEvent = Meziantou.Framework.Yaml.Events.Scalar;

namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>
/// Provides YAML scalar parsing helpers aligned with the YAML 1.2 core schema conventions used by <see cref="YamlSerializer"/>.
/// </summary>
public static class YamlScalar
{
    private static readonly ExtendedSchema ExtendedSchema = new();
    private static readonly FailsafeSchema FailsafeSchema = new();
    private static readonly JsonSchema JsonSchema = new();

    /// <summary>
    /// Determines whether a scalar represents YAML null (for example <c language="yaml">null</c> or <c language="yaml">~</c>).
    /// </summary>
    public static bool IsNull(ReadOnlySpan<char> value)
    {
        value = Trim(value);
        if (value.Length == 0)
        {
            return true;
        }

        return value is "~" or "null" or "Null" or "NULL";
    }

    /// <summary>
    /// Parses a YAML boolean scalar (<c>true</c>/<c>false</c>).
    /// </summary>
    public static bool TryParseBool(ReadOnlySpan<char> value, out bool result)
    {
        value = Trim(value);
        if (value is "true" or "True" or "TRUE")
        {
            result = true;
            return true;
        }

        if (value is "false" or "False" or "FALSE")
        {
            result = false;
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>
    /// Parses a YAML integer scalar into <see cref="int"/>.
    /// </summary>
    public static bool TryParseInt32(ReadOnlySpan<char> value, out int result)
    {
        if (TryParseInt64(value, out var longValue) && longValue is >= int.MinValue and <= int.MaxValue)
        {
            result = (int)longValue;
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>
    /// Parses a YAML integer scalar into <see cref="uint"/>.
    /// </summary>
    public static bool TryParseUInt32(ReadOnlySpan<char> value, out uint result)
    {
        if (TryParseUInt64(value, out var ulongValue) && ulongValue <= uint.MaxValue)
        {
            result = (uint)ulongValue;
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>
    /// Parses a YAML integer scalar into <see cref="ulong"/>, including common base prefixes (<c>0x</c>, <c>0o</c>, <c>0b</c>) and underscores.
    /// </summary>
    public static bool TryParseUInt64(ReadOnlySpan<char> value, out ulong result)
    {
        value = Trim(value);
        if (value.Length == 0)
        {
            result = default;
            return false;
        }

        if (value[0] == '-')
        {
            result = default;
            return false;
        }

        ReadOnlySpan<char> cleaned = value;
        if (ContainsChar(value, '_'))
        {
            var buffer = new char[value.Length];
            var written = 0;
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c != '_')
                {
                    buffer[written++] = c;
                }
            }

            var underscoreRemoved = new string(buffer, 0, written);
            cleaned = underscoreRemoved.AsSpan();
        }

        if (cleaned.Length > 0 && cleaned[0] == '+')
        {
            cleaned = cleaned.Slice(1);
        }

        if (cleaned.Length >= 2 && cleaned[0] == '0')
        {
            var prefix = cleaned[1];
            if (prefix == 'x')
            {
                return TryParseUInt64Base(cleaned.Slice(2), 16, out result);
            }

            if (prefix == 'o')
            {
                return TryParseUInt64Base(cleaned.Slice(2), 8, out result);
            }

            if (prefix == 'b')
            {
                return TryParseUInt64Base(cleaned.Slice(2), 2, out result);
            }
        }

        return ulong.TryParse(cleaned, NumberStyles.None, CultureInfo.InvariantCulture, out result);
    }

    /// <summary>
    /// Parses a YAML floating-point scalar into <see cref="decimal"/>.
    /// </summary>
    public static bool TryParseDecimal(ReadOnlySpan<char> value, out decimal result)
    {
        value = Trim(value);
        if (value.Length == 0)
        {
            result = default;
            return false;
        }

        if (!IsNumberLike(value))
        {
            result = default;
            return false;
        }

        if (ContainsChar(value, '_'))
        {
            var buffer = new char[value.Length];
            var written = 0;
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c != '_')
                {
                    buffer[written++] = c;
                }
            }

            var underscoreRemoved = new string(buffer, 0, written);
            return decimal.TryParse(underscoreRemoved, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        return decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    /// <summary>
    /// Parses a YAML integer scalar into <see cref="long"/>, including common base prefixes (<c>0x</c>, <c>0o</c>, <c>0b</c>) and underscores.
    /// </summary>
    public static bool TryParseInt64(ReadOnlySpan<char> value, out long result)
    {
        value = Trim(value);
        if (value.Length == 0)
        {
            result = default;
            return false;
        }

        ReadOnlySpan<char> cleaned = value;
        if (ContainsChar(value, '_'))
        {
            var buffer = new char[value.Length];
            var written = 0;
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c != '_')
                {
                    buffer[written++] = c;
                }
            }

            var underscoreRemoved = new string(buffer, 0, written);
            cleaned = underscoreRemoved.AsSpan();
        }

        var sign = 1;
        if (cleaned.Length > 0 && (cleaned[0] == '+' || cleaned[0] == '-'))
        {
            if (cleaned[0] == '-')
            {
                sign = -1;
            }

            cleaned = cleaned.Slice(1);
        }

        if (cleaned.Length == 0)
        {
            result = default;
            return false;
        }

        ulong magnitude;
        if (cleaned.Length >= 2 && cleaned[0] == '0')
        {
            var prefix = cleaned[1];
            if (prefix == 'x')
            {
                if (!TryParseUInt64Base(cleaned.Slice(2), 16, out magnitude))
                {
                    result = default;
                    return false;
                }

                return TryApplySignedMagnitude(magnitude, sign, out result);
            }

            if (prefix == 'o')
            {
                if (!TryParseUInt64Base(cleaned.Slice(2), 8, out magnitude))
                {
                    result = default;
                    return false;
                }

                return TryApplySignedMagnitude(magnitude, sign, out result);
            }

            if (prefix == 'b')
            {
                if (!TryParseUInt64Base(cleaned.Slice(2), 2, out magnitude))
                {
                    result = default;
                    return false;
                }

                return TryApplySignedMagnitude(magnitude, sign, out result);
            }
        }

        if (!ulong.TryParse(cleaned, NumberStyles.None, CultureInfo.InvariantCulture, out magnitude))
        {
            result = default;
            return false;
        }

        return TryApplySignedMagnitude(magnitude, sign, out result);
    }

    /// <summary>
    /// Parses a YAML floating-point scalar into <see cref="double"/>, including <c>.inf</c> and <c>.nan</c>.
    /// </summary>
    public static bool TryParseDouble(ReadOnlySpan<char> value, out double result)
    {
        value = Trim(value);

        if (value is ".inf" or ".Inf" or ".INF" or "+.inf" or "+.Inf" or "+.INF")
        {
            result = double.PositiveInfinity;
            return true;
        }

        if (value is "-.inf" or "-.Inf" or "-.INF")
        {
            result = double.NegativeInfinity;
            return true;
        }

        if (value is ".nan" or ".NaN" or ".NAN")
        {
            result = double.NaN;
            return true;
        }

        // .NET also parses its own spellings, such as "NaN" or "-Infinity", which are strings in YAML.
        if (!IsNumberLike(value))
        {
            result = default;
            return false;
        }

        if (ContainsChar(value, '_'))
        {
            var buffer = new char[value.Length];
            var written = 0;
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c != '_')
                {
                    buffer[written++] = c;
                }
            }

            var underscoreRemovedDouble = new string(buffer, 0, written);
            return double.TryParse(underscoreRemovedDouble, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    /// <summary>
    /// Parses a YAML scalar as a <see cref="CultureInfo"/> from its culture name (for example <c language="yaml">fr-FR</c>).
    /// </summary>
    /// <param name="value">The culture name to parse. An empty name resolves to <see cref="CultureInfo.InvariantCulture"/>.</param>
    /// <param name="result">The parsed culture.</param>
    public static bool TryParseCultureInfo(string? value, [NotNullWhen(true)] out CultureInfo? result)
    {
        if (value is null)
        {
            result = null;
            return false;
        }

        try
        {
            result = CultureInfo.GetCultureInfo(value);
            return true;
        }
        catch (CultureNotFoundException)
        {
            result = null;
            return false;
        }
    }

#if NET11_0_OR_GREATER
    /// <summary>
    /// Parses a YAML floating-point scalar into an IEEE 754 floating-point type, including <c>.inf</c> and <c>.nan</c>.
    /// </summary>
    /// <typeparam name="T">The IEEE 754 floating-point type to parse.</typeparam>
    /// <param name="value">The scalar text to parse.</param>
    /// <param name="result">The parsed floating-point value.</param>
    public static bool TryParseIeee754<T>(ReadOnlySpan<char> value, out T result)
        where T : struct, IFloatingPointIeee754<T>
    {
        value = Trim(value);

        if (value is ".inf" or ".Inf" or ".INF" or "+.inf" or "+.Inf" or "+.INF")
        {
            result = T.PositiveInfinity;
            return true;
        }

        if (value is "-.inf" or "-.Inf" or "-.INF")
        {
            result = T.NegativeInfinity;
            return true;
        }

        if (value is ".nan" or ".NaN" or ".NAN")
        {
            result = T.NaN;
            return true;
        }

        // .NET also parses its own spellings, such as "NaN" or "-Infinity", which are strings in YAML.
        if (!IsNumberLike(value))
        {
            result = default;
            return false;
        }

        if (ContainsChar(value, '_'))
        {
            var buffer = new char[value.Length];
            var written = 0;
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c != '_')
                {
                    buffer[written++] = c;
                }
            }

            return T.TryParse(buffer.AsSpan(0, written), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        return T.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }
#endif

    /// <summary>
    /// Determines whether the current scalar token represents YAML null while honoring scalar style and <see cref="YamlSerializerOptions.UseSchema"/>.
    /// </summary>
    /// <param name="reader">The reader positioned on a scalar token.</param>
    public static bool IsNull(YamlReader reader)
    {
        if (reader.TokenType != YamlTokenType.Scalar)
        {
            return false;
        }

        if (reader.Options.UseSchema)
        {
            return TryResolveSchemaScalar(reader, out var defaultTag, out var value) &&
                   string.Equals(defaultTag, JsonSchema.NullShortTag, StringComparison.Ordinal) &&
                   value is null;
        }

        // An explicit tag other than !!null, such as "!!int" on an empty scalar, is not null even if the text is.
        if (HasStringTag(reader) || (HasCoreScalarTag(reader) && !IsNullTag(reader)))
        {
            return false;
        }

        return IsNull(reader.ScalarValue.AsSpan(), reader.ScalarStyle);
    }

    /// <summary>
    /// Parses the current scalar token as a YAML boolean while honoring scalar style and <see cref="YamlSerializerOptions.UseSchema"/>.
    /// </summary>
    /// <param name="reader">The reader positioned on a scalar token.</param>
    /// <param name="result">The parsed boolean value.</param>
    public static bool TryParseBool(YamlReader reader, out bool result)
    {
        if (reader.Options.UseSchema)
        {
            if (TryResolveSchemaScalar(reader, out var defaultTag, out var value) &&
                string.Equals(defaultTag, JsonSchema.BoolShortTag, StringComparison.Ordinal) &&
                value is bool boolean)
            {
                result = boolean;
                return true;
            }

            result = default;
            return false;
        }

        return TryParseBool(reader.ScalarValue.AsSpan(), out result);
    }

    /// <summary>
    /// Parses the current scalar token as a YAML integer while honoring scalar style and <see cref="YamlSerializerOptions.UseSchema"/>.
    /// </summary>
    /// <param name="reader">The reader positioned on a scalar token.</param>
    /// <param name="result">The parsed integer value.</param>
    public static bool TryParseInt32(YamlReader reader, out int result)
    {
        if (reader.Options.UseSchema)
        {
            if (TryResolveSchemaScalar(reader, out var defaultTag, out var value) &&
                string.Equals(defaultTag, JsonSchema.IntShortTag, StringComparison.Ordinal) &&
                TryConvertToInt64(value, out var parsed) &&
                parsed is >= int.MinValue and <= int.MaxValue)
            {
                result = (int)parsed;
                return true;
            }

            result = default;
            return false;
        }

        return TryParseInt32(reader.ScalarValue.AsSpan(), out result);
    }

    /// <summary>
    /// Parses the current scalar token as an unsigned YAML integer while honoring scalar style and <see cref="YamlSerializerOptions.UseSchema"/>.
    /// </summary>
    /// <param name="reader">The reader positioned on a scalar token.</param>
    /// <param name="result">The parsed unsigned integer value.</param>
    public static bool TryParseUInt32(YamlReader reader, out uint result)
    {
        if (reader.Options.UseSchema)
        {
            if (TryResolveSchemaScalar(reader, out var defaultTag, out var value) &&
                string.Equals(defaultTag, JsonSchema.IntShortTag, StringComparison.Ordinal) &&
                TryConvertToUInt64(value, out var parsed) &&
                parsed <= uint.MaxValue)
            {
                result = (uint)parsed;
                return true;
            }

            result = default;
            return false;
        }

        return TryParseUInt32(reader.ScalarValue.AsSpan(), out result);
    }

    /// <summary>
    /// Parses the current scalar token as an unsigned YAML integer while honoring scalar style and <see cref="YamlSerializerOptions.UseSchema"/>.
    /// </summary>
    /// <param name="reader">The reader positioned on a scalar token.</param>
    /// <param name="result">The parsed unsigned integer value.</param>
    public static bool TryParseUInt64(YamlReader reader, out ulong result)
    {
        if (reader.Options.UseSchema)
        {
            if (TryResolveSchemaScalar(reader, out var defaultTag, out var value) &&
                string.Equals(defaultTag, JsonSchema.IntShortTag, StringComparison.Ordinal) &&
                TryConvertToUInt64(value, out result))
            {
                return true;
            }

            result = default;
            return false;
        }

        return TryParseUInt64(reader.ScalarValue.AsSpan(), out result);
    }

    /// <summary>
    /// Parses the current scalar token as a YAML integer while honoring scalar style and <see cref="YamlSerializerOptions.UseSchema"/>.
    /// </summary>
    /// <param name="reader">The reader positioned on a scalar token.</param>
    /// <param name="result">The parsed integer value.</param>
    public static bool TryParseInt64(YamlReader reader, out long result)
    {
        if (reader.Options.UseSchema)
        {
            if (TryResolveSchemaScalar(reader, out var defaultTag, out var value) &&
                string.Equals(defaultTag, JsonSchema.IntShortTag, StringComparison.Ordinal) &&
                TryConvertToInt64(value, out result))
            {
                return true;
            }

            result = default;
            return false;
        }

        return TryParseInt64(reader.ScalarValue.AsSpan(), out result);
    }

    /// <summary>
    /// Parses the current scalar token as a YAML floating-point value while honoring scalar style and <see cref="YamlSerializerOptions.UseSchema"/>.
    /// </summary>
    /// <param name="reader">The reader positioned on a scalar token.</param>
    /// <param name="result">The parsed floating-point value.</param>
    public static bool TryParseDouble(YamlReader reader, out double result)
    {
        if (reader.Options.UseSchema)
        {
            if (TryResolveSchemaScalar(reader, out var defaultTag, out var value) &&
                (string.Equals(defaultTag, JsonSchema.FloatShortTag, StringComparison.Ordinal) ||
                 string.Equals(defaultTag, JsonSchema.IntShortTag, StringComparison.Ordinal)) &&
                TryConvertToDouble(value, out result))
            {
                return true;
            }

            result = default;
            return false;
        }

        return TryParseDouble(reader.ScalarValue.AsSpan(), out result);
    }

    /// <summary>
    /// Parses the current scalar token as a YAML decimal value while honoring scalar style and <see cref="YamlSerializerOptions.UseSchema"/>.
    /// </summary>
    /// <param name="reader">The reader positioned on a scalar token.</param>
    /// <param name="result">The parsed decimal value.</param>
    public static bool TryParseDecimal(YamlReader reader, out decimal result)
    {
        if (reader.Options.UseSchema)
        {
            if (TryResolveSchemaScalar(reader, out var defaultTag, out var value) &&
                (string.Equals(defaultTag, JsonSchema.FloatShortTag, StringComparison.Ordinal) ||
                 string.Equals(defaultTag, JsonSchema.IntShortTag, StringComparison.Ordinal)))
            {
                // An integer the schema decoded into an integral type converts exactly, and it is the only form
                // that carries the base the scalar was written in. Any other value went through a floating-point
                // type, which drops digits a decimal can hold, so the original text is parsed instead.
                if (IsIntegral(value))
                {
                    return TryConvertToDecimal(value, out result);
                }

                if (TryParseDecimal(reader.ScalarValue.AsSpan(), out result))
                {
                    return true;
                }

                return TryConvertToDecimal(value, out result);
            }

            result = default;
            return false;
        }

        return TryParseDecimal(reader.ScalarValue.AsSpan(), out result);
    }

#if NET11_0_OR_GREATER
    /// <summary>
    /// Parses the current scalar token as an IEEE 754 floating-point value while honoring scalar style and <see cref="YamlSerializerOptions.UseSchema"/>.
    /// </summary>
    /// <typeparam name="T">The IEEE 754 floating-point type to parse.</typeparam>
    /// <param name="reader">The reader positioned on a scalar token.</param>
    /// <param name="result">The parsed floating-point value.</param>
    public static bool TryParseIeee754<T>(YamlReader reader, out T result)
        where T : struct, IFloatingPointIeee754<T>
    {
        if (reader.Options.UseSchema)
        {
            // The schema resolves scalars to double or decimal, which cannot represent every value of the
            // wider IEEE 754 types. Only the resolved tag is used, the text is parsed by the target type.
            if (TryResolveSchemaScalar(reader, out var defaultTag, out _) &&
                (string.Equals(defaultTag, JsonSchema.FloatShortTag, StringComparison.Ordinal) ||
                 string.Equals(defaultTag, JsonSchema.IntShortTag, StringComparison.Ordinal)))
            {
                return TryParseIeee754(reader.ScalarValue.AsSpan(), out result);
            }

            result = default;
            return false;
        }

        return TryParseIeee754(reader.ScalarValue.AsSpan(), out result);
    }
#endif

    /// <summary>Resolves the current scalar token to the CLR value implied by the scalar style, tag, and schema options.</summary>
    /// <param name="reader">The reader positioned on a scalar token.</param>
    public static object? ResolveObject(YamlReader reader)
    {
        if (reader.Options.UseSchema || HasCoreScalarTag(reader))
        {
            if (TryResolveSchemaScalar(reader, out var tag, out var value))
            {
                return value;
            }

            // A node whose content does not match its explicit tag, such as "!!int abc", is invalid.
            if (HasCoreScalarTag(reader))
            {
                throw YamlThrowHelper.ThrowInvalidScalar(reader, $"The scalar '{reader.ScalarValue}' is not a valid '{tag}' value.");
            }

            return reader.ScalarValue ?? string.Empty;
        }

        if (!IsPlainStyle(reader.ScalarStyle) || HasStringTag(reader))
        {
            return reader.ScalarValue ?? string.Empty;
        }

        var text = reader.ScalarValue.AsSpan();
        if (IsNull(text))
        {
            return null;
        }

        if (TryParseBool(text, out var boolean))
        {
            return boolean;
        }

        if (TryParseInt64(text, out var integer))
        {
            return integer;
        }

        if (TryParseUInt64(text, out var unsignedInteger))
        {
            return unsignedInteger;
        }

        if (TryParseDouble(text, out var floating))
        {
            return floating;
        }

        return reader.ScalarValue ?? string.Empty;
    }

    /// <summary>Formats a binary floating-point value as a YAML float.</summary>
    /// <remarks>
    /// .NET spells the special values "Infinity" and "NaN", which are strings in YAML, writes negative zero as "-0",
    /// which is the integer 0, and writes a whole number without a fraction, which is an integer too.
    /// </remarks>
    internal static string FormatFloatingPoint(double value)
    {
        return value switch
        {
            double.PositiveInfinity => ".inf",
            double.NegativeInfinity => "-.inf",
            _ when double.IsNaN(value) => ".nan",
            0 when double.IsNegative(value) => "-0.0",
            _ => EnsureFraction(value.ToString("R", CultureInfo.InvariantCulture)),
        };
    }

    /// <inheritdoc cref="FormatFloatingPoint(double)"/>
    internal static string FormatFloatingPoint(float value)
    {
        return float.IsFinite(value) && !(value == 0 && float.IsNegative(value))
            ? EnsureFraction(value.ToString("R", CultureInfo.InvariantCulture))
            : FormatFloatingPoint((double)value);
    }

    /// <inheritdoc cref="FormatFloatingPoint(double)"/>
    internal static string FormatFloatingPoint(Half value)
    {
        return Half.IsFinite(value) && !(value == Half.Zero && Half.IsNegative(value))
            ? EnsureFraction(value.ToString(CultureInfo.InvariantCulture))
            : FormatFloatingPoint((double)value);
    }

    private static string EnsureFraction(string value)
        => value.AsSpan().IndexOfAny('.', 'E', 'e') < 0 ? value + ".0" : value;

    /// <summary>Determines whether a plain scalar with this text resolves to a type other than a string under the schema.</summary>
    /// <remarks>
    /// This covers the spellings the span-based parsers reject, such as an integer beyond the range of <see cref="ulong"/>,
    /// or the booleans and timestamps of the extended schema.
    /// </remarks>
    internal static bool ResolvesToNonString(ReadOnlySpan<char> value, YamlSchemaKind schemaKind)
    {
        // Every non-string rule of the built-in schemas matches an empty scalar or starts with one of these characters,
        // which avoids evaluating the rules for most strings.
        if (schemaKind is YamlSchemaKind.Failsafe || (value.Length > 0 && !char.IsAsciiDigit(value[0]) && "+-.~<nNtTfFyYoO".IndexOf(value[0], StringComparison.Ordinal) < 0))
        {
            return false;
        }

        var scalar = new ScalarEvent(anchor: null, tag: null, value.ToString(), ScalarStyle.Plain, isPlainImplicit: true, isQuotedImplicit: false);
        return GetSchema(schemaKind).TryParse(scalar, decodeValue: false, out var tag, out _) &&
               !string.Equals(tag, SchemaBase.StrShortTag, StringComparison.Ordinal);
    }

    private static bool IsNull(ReadOnlySpan<char> value, ScalarStyle style) => IsPlainStyle(style) && IsNull(value);

    /// <remarks>The non-specific tag "!" also makes a scalar a string (YAML 1.2 §6.9.1).</remarks>
    private static bool HasStringTag(YamlReader reader)
    {
        if (reader.Tag is null)
        {
            return false;
        }

        if (reader.Tag == "!")
        {
            return true;
        }

        var schema = GetSchema(reader.Options.Schema);
        return string.Equals(schema.ShortenTag(reader.Tag), SchemaBase.StrShortTag, StringComparison.Ordinal);
    }

    private static bool IsNullTag(YamlReader reader)
        => reader.Tag is not null && string.Equals(GetSchema(reader.Options.Schema).ShortenTag(reader.Tag), JsonSchema.NullShortTag, StringComparison.Ordinal);

    /// <summary>Determines whether the scalar has an explicit <c>!!null</c>, <c>!!bool</c>, <c>!!int</c> or <c>!!float</c> tag that the schema in use defines.</summary>
    private static bool HasCoreScalarTag(YamlReader reader)
    {
        if (reader.Tag is null || reader.Options.Schema is YamlSchemaKind.Failsafe)
        {
            return false;
        }

        var shortTag = GetSchema(reader.Options.Schema).ShortenTag(reader.Tag);
        return shortTag is JsonSchema.NullShortTag or JsonSchema.BoolShortTag or JsonSchema.IntShortTag or JsonSchema.FloatShortTag;
    }

    private static bool IsPlainStyle(ScalarStyle style) => style is ScalarStyle.Any or ScalarStyle.Plain;

    private static bool TryResolveSchemaScalar(YamlReader reader, out string? defaultTag, out object? value)
    {
        var schema = GetSchema(reader.Options.Schema);
        var scalar = CreateScalar(reader);
        if (reader.Tag is not null)
        {
            var shortTag = schema.ShortenTag(reader.Tag);
            if (shortTag is not null)
            {
                if (TryResolveExplicitTag(schema, shortTag, scalar, out value))
                {
                    defaultTag = shortTag;
                    return true;
                }

                defaultTag = shortTag;
                value = null;
                return false;
            }
        }

        return schema.TryParse(scalar, true, out defaultTag, out value);
    }

    private static bool TryResolveExplicitTag(FailsafeSchema schema, string shortTag, ScalarEvent scalar, out object? value)
    {
        if (string.Equals(shortTag, SchemaBase.StrShortTag, StringComparison.Ordinal))
        {
            value = scalar.Value;
            return true;
        }

        var plainScalar = IsPlainStyle(scalar.Style)
            ? scalar
            : new ScalarEvent(
                scalar.Anchor,
                scalar.Tag,
                scalar.Value,
                ScalarStyle.Plain,
                scalar.IsPlainImplicit,
                scalar.IsQuotedImplicit,
                scalar.Start,
                scalar.End);

        if (schema.TryParse(plainScalar, true, out var resolvedTag, out value) && string.Equals(resolvedTag, shortTag, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(shortTag, JsonSchema.FloatShortTag, StringComparison.Ordinal) && schema is JsonSchema)
        {
            // An explicit float tag also accepts integer spellings; implicit resolution tries integer rules first.
            if (schema.TryParse(plainScalar, typeof(double), out value) ||
                (schema is ExtendedSchema && CoreSchema.Instance.TryParse(plainScalar, typeof(double), out value)))
            {
                return true;
            }

            // The JSON schema defines these canonical float values, but does not resolve them implicitly.
            value = scalar.Value switch
            {
                ".inf" => double.PositiveInfinity,
                "-.inf" => double.NegativeInfinity,
                ".nan" => double.NaN,
                _ => null,
            };
            if (value is not null)
            {
                return true;
            }
        }

        if (string.Equals(shortTag, JsonSchema.NullShortTag, StringComparison.Ordinal) && IsNull(scalar.Value.AsSpan()))
        {
            value = null;
            return true;
        }

        value = null;
        return false;
    }

    private static ScalarEvent CreateScalar(YamlReader reader)
        => new(
            reader.Anchor,
            reader.Tag,
            reader.ScalarValue ?? string.Empty,
            reader.ScalarStyle,
            isPlainImplicit: IsPlainStyle(reader.ScalarStyle),
            isQuotedImplicit: !IsPlainStyle(reader.ScalarStyle),
            reader.Start,
            reader.End);

    private static FailsafeSchema GetSchema(YamlSchemaKind schemaKind)
        => schemaKind switch
        {
            YamlSchemaKind.Json => JsonSchema,
            YamlSchemaKind.Failsafe => FailsafeSchema,
            YamlSchemaKind.Extended => ExtendedSchema,
            _ => CoreSchema.Instance,
        };

    private static bool IsIntegral(object? value) => value is int or long or ulong;

    private static bool TryConvertToInt64(object? value, out long result)
    {
        switch (value)
        {
            case int intValue:
                result = intValue;
                return true;
            case long longValue:
                result = longValue;
                return true;
            case ulong ulongValue when ulongValue <= (ulong)long.MaxValue:
                result = (long)ulongValue;
                return true;
            default:
                result = default;
                return false;
        }
    }

    private static bool TryConvertToUInt64(object? value, out ulong result)
    {
        switch (value)
        {
            case int intValue when intValue >= 0:
                result = (ulong)intValue;
                return true;
            case long longValue when longValue >= 0:
                result = (ulong)longValue;
                return true;
            case ulong ulongValue:
                result = ulongValue;
                return true;
            default:
                result = default;
                return false;
        }
    }

    private static bool TryConvertToDouble(object? value, out double result)
    {
        switch (value)
        {
            case int intValue:
                result = intValue;
                return true;
            case long longValue:
                result = longValue;
                return true;
            case ulong ulongValue:
                result = ulongValue;
                return true;
            case float floatValue:
                result = floatValue;
                return true;
            case double doubleValue:
                result = doubleValue;
                return true;
            case decimal decimalValue:
                result = (double)decimalValue;
                return true;
            default:
                result = default;
                return false;
        }
    }

    private static bool TryConvertToDecimal(object? value, out decimal result)
    {
        switch (value)
        {
            case int intValue:
                result = intValue;
                return true;
            case long longValue:
                result = longValue;
                return true;
            case ulong ulongValue:
                result = ulongValue;
                return true;
            case float floatValue when !float.IsNaN(floatValue) && !float.IsInfinity(floatValue):
                result = (decimal)floatValue;
                return true;
            case double doubleValue when !double.IsNaN(doubleValue) && !double.IsInfinity(doubleValue):
                result = (decimal)doubleValue;
                return true;
            case decimal decimalValue:
                result = decimalValue;
                return true;
            default:
                result = default;
                return false;
        }
    }

    /// <remarks>
    /// Only YAML white space (space and tab) is removed: any other character, such as a no-break space, is content.
    /// </remarks>
    private static ReadOnlySpan<char> Trim(ReadOnlySpan<char> value) => value.Trim(" \t");

    /// <remarks>
    /// A number starts with a digit or a dot, optionally after a sign. This excludes the names .NET accepts for
    /// special values, such as "NaN" or "Infinity", and the leading white space of its number styles.
    /// </remarks>
    private static bool IsNumberLike(ReadOnlySpan<char> value)
    {
        if (value.Length > 0 && value[0] is '+' or '-')
        {
            value = value.Slice(1);
        }

        return value.Length > 0 && (char.IsAsciiDigit(value[0]) || value[0] == '.');
    }

    private static bool ContainsChar(ReadOnlySpan<char> value, char c)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == c)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryApplySignedMagnitude(ulong magnitude, int sign, out long result)
    {
        if (sign >= 0)
        {
            if (magnitude > (ulong)long.MaxValue)
            {
                result = default;
                return false;
            }

            result = (long)magnitude;
            return true;
        }

        var maxNegativeMagnitude = (ulong)long.MaxValue + 1;
        if (magnitude > maxNegativeMagnitude)
        {
            result = default;
            return false;
        }

        if (magnitude == maxNegativeMagnitude)
        {
            result = long.MinValue;
            return true;
        }

        result = -(long)magnitude;
        return true;
    }

    private static bool TryParseUInt64Base(ReadOnlySpan<char> value, int numberBase, out ulong result)
    {
        if (value.Length == 0)
        {
            result = default;
            return false;
        }

        ulong accumulator = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            int digit;
            if (c is >= '0' and <= '9')
            {
                digit = c - '0';
            }
            else if (c is >= 'a' and <= 'f')
            {
                digit = 10 + (c - 'a');
            }
            else if (c is >= 'A' and <= 'F')
            {
                digit = 10 + (c - 'A');
            }
            else
            {
                result = default;
                return false;
            }

            if (digit >= numberBase)
            {
                result = default;
                return false;
            }

            // The value can exceed ulong.MaxValue, and this method must report that as a failed parse
            // instead of throwing, so the overflow is detected before the multiplication happens.
            var radix = (ulong)numberBase;
            if (accumulator > (ulong.MaxValue - (ulong)digit) / radix)
            {
                result = default;
                return false;
            }

            accumulator = (accumulator * radix) + (ulong)digit;
        }

        result = accumulator;
        return true;
    }
}
