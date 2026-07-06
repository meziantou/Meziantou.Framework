#nullable enable // The file is embedded in the source generator, so we need to enable nullable reference types to avoid warnings in the generated code.
namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>
/// Helper methods for throwing <see cref="YamlException"/> with location context.
/// </summary>
[Microsoft.CodeAnalysis.Embedded]
internal static class YamlThrowHelper
{
    /// <summary>Throws an exception for expected Token.</summary>
    public static YamlException ThrowExpectedToken(YamlReader reader, YamlTokenType expectedToken)
        => new(reader.SourceName, reader.Start, reader.End, $"Expected a {expectedToken} token but found '{reader.TokenType}'.");

    /// <summary>Throws an exception for expected Scalar.</summary>
    public static YamlException ThrowExpectedScalar(YamlReader reader)
        => ThrowExpectedToken(reader, YamlTokenType.Scalar);

    /// <summary>Throws an exception for expected Scalar Key.</summary>
    public static YamlException ThrowExpectedScalarKey(YamlReader reader)
        => new(reader.SourceName, reader.Start, reader.End, $"Expected a scalar key token but found '{reader.TokenType}'.");

    /// <summary>Throws an exception for expected Mapping.</summary>
    public static YamlException ThrowExpectedMapping(YamlReader reader)
        => ThrowExpectedToken(reader, YamlTokenType.StartMapping);

    /// <summary>Throws an exception for expected Sequence.</summary>
    public static YamlException ThrowExpectedSequence(YamlReader reader)
        => ThrowExpectedToken(reader, YamlTokenType.StartSequence);

    /// <summary>Throws an exception for unexpected Token.</summary>
    public static YamlException ThrowUnexpectedToken(YamlReader reader)
        => new(reader.SourceName, reader.Start, reader.End, $"Unexpected token '{reader.TokenType}'.");

    /// <summary>Throws an exception for not Supported.</summary>
    public static YamlException ThrowNotSupported(YamlReader reader, string message)
        => new(reader.SourceName, reader.Start, reader.End, message);

    /// <summary>Throws an exception for duplicate Mapping Key.</summary>
    public static YamlException ThrowDuplicateMappingKey(YamlReader reader, string key)
        => new(reader.SourceName, reader.Start, reader.End, $"Duplicate mapping key '{key}'.");

    /// <summary>Throws an exception for unknown Type Discriminator.</summary>
    public static YamlException ThrowUnknownTypeDiscriminator(YamlReader reader, string? discriminatorValue, Type baseType)
        => new(reader.SourceName, reader.Start, reader.End, $"Unknown type discriminator '{discriminatorValue}' for '{baseType}'.");

    /// <summary>Throws an exception for unknown Type Tag.</summary>
    public static YamlException ThrowUnknownTypeTag(YamlReader reader, string? tag, Type baseType)
        => new(reader.SourceName, reader.Start, reader.End, $"Unknown type tag '{tag}' for '{baseType}'.");

    /// <summary>Throws an exception for abstract Type Without Discriminator.</summary>
    public static YamlException ThrowAbstractTypeWithoutDiscriminator(YamlReader reader, Type type)
        => new(reader.SourceName, reader.Start, reader.End, $"Cannot deserialize abstract type '{type}' without a known derived type discriminator.");

    /// <summary>Throws an exception for expected Discriminator Scalar.</summary>
    public static YamlException ThrowExpectedDiscriminatorScalar(YamlReader reader, string discriminatorPropertyName)
        => new(reader.SourceName, reader.Start, reader.End, $"Expected '{discriminatorPropertyName}' to be a scalar discriminator.");

    /// <summary>Throws an exception for invalid Scalar.</summary>
    public static YamlException ThrowInvalidScalar(YamlReader reader, string message)
        => new(reader.SourceName, reader.Start, reader.End, message);

    /// <summary>Throws an exception for invalid Boolean Scalar.</summary>
    public static YamlException ThrowInvalidBooleanScalar(YamlReader reader)
        => ThrowInvalidScalar(reader, $"Invalid boolean scalar '{reader.ScalarValue}'.");

    /// <summary>Throws an exception for invalid Integer Scalar.</summary>
    public static YamlException ThrowInvalidIntegerScalar(YamlReader reader)
        => ThrowInvalidScalar(reader, $"Invalid integer scalar '{reader.ScalarValue}'.");

    /// <summary>Throws an exception for invalid U Int32 Scalar.</summary>
    public static YamlException ThrowInvalidUInt32Scalar(YamlReader reader)
        => ThrowInvalidScalar(reader, $"Invalid uint32 scalar '{reader.ScalarValue}'.");

    /// <summary>Throws an exception for invalid U Int64 Scalar.</summary>
    public static YamlException ThrowInvalidUInt64Scalar(YamlReader reader)
        => ThrowInvalidScalar(reader, $"Invalid uint64 scalar '{reader.ScalarValue}'.");

    /// <summary>Throws an exception for invalid Byte Scalar.</summary>
    public static YamlException ThrowInvalidByteScalar(YamlReader reader)
        => ThrowInvalidScalar(reader, $"Invalid byte scalar '{reader.ScalarValue}'.");

    /// <summary>Throws an exception for invalid S Byte Scalar.</summary>
    public static YamlException ThrowInvalidSByteScalar(YamlReader reader)
        => ThrowInvalidScalar(reader, $"Invalid sbyte scalar '{reader.ScalarValue}'.");

    /// <summary>Throws an exception for invalid Int16 Scalar.</summary>
    public static YamlException ThrowInvalidInt16Scalar(YamlReader reader)
        => ThrowInvalidScalar(reader, $"Invalid int16 scalar '{reader.ScalarValue}'.");

    /// <summary>Throws an exception for invalid U Int16 Scalar.</summary>
    public static YamlException ThrowInvalidUInt16Scalar(YamlReader reader)
        => ThrowInvalidScalar(reader, $"Invalid uint16 scalar '{reader.ScalarValue}'.");

    /// <summary>Throws an exception for invalid N Int Scalar.</summary>
    public static YamlException ThrowInvalidNIntScalar(YamlReader reader)
        => ThrowInvalidScalar(reader, $"Invalid nint scalar '{reader.ScalarValue}'.");

    /// <summary>Throws an exception for invalid NU Int Scalar.</summary>
    public static YamlException ThrowInvalidNUIntScalar(YamlReader reader)
        => ThrowInvalidScalar(reader, $"Invalid nuint scalar '{reader.ScalarValue}'.");

    /// <summary>Throws an exception for invalid Float Scalar.</summary>
    public static YamlException ThrowInvalidFloatScalar(YamlReader reader)
        => ThrowInvalidScalar(reader, $"Invalid float scalar '{reader.ScalarValue}'.");

    /// <summary>Throws an exception for invalid Decimal Scalar.</summary>
    public static YamlException ThrowInvalidDecimalScalar(YamlReader reader)
        => ThrowInvalidScalar(reader, $"Invalid decimal scalar '{reader.ScalarValue}'.");

    /// <summary>Throws an exception for invalid Char Scalar.</summary>
    public static YamlException ThrowInvalidCharScalar(YamlReader reader, string text)
        => ThrowInvalidScalar(reader, $"Invalid char scalar '{text}'.");

    /// <summary>Throws an exception for invalid Enum Scalar.</summary>
    public static YamlException ThrowInvalidEnumScalar(YamlReader reader, string text)
        => ThrowInvalidScalar(reader, $"Invalid enum scalar '{text}'.");

    /// <summary>Throws an exception for invalid <see cref="DateTime"/> Scalar.</summary>
    public static YamlException ThrowInvalidDateTimeScalar(YamlReader reader)
        => ThrowInvalidScalar(reader, $"Invalid DateTime scalar '{reader.ScalarValue}'.");

    /// <summary>Throws an exception for invalid <see cref="DateTimeOffset"/> Scalar.</summary>
    public static YamlException ThrowInvalidDateTimeOffsetScalar(YamlReader reader)
        => ThrowInvalidScalar(reader, $"Invalid DateTimeOffset scalar '{reader.ScalarValue}'.");

    /// <summary>Throws an exception for invalid <see cref="Guid"/> Scalar.</summary>
    public static YamlException ThrowInvalidGuidScalar(YamlReader reader)
        => ThrowInvalidScalar(reader, $"Invalid Guid scalar '{reader.ScalarValue}'.");

    /// <summary>Throws an exception for invalid <see cref="TimeSpan"/> Scalar.</summary>
    public static YamlException ThrowInvalidTimeSpanScalar(YamlReader reader)
        => ThrowInvalidScalar(reader, $"Invalid TimeSpan scalar '{reader.ScalarValue}'.");

    /// <summary>Throws an exception for invalid <see cref="DateOnly"/> Scalar.</summary>
    public static YamlException ThrowInvalidDateOnlyScalar(YamlReader reader)
        => ThrowInvalidScalar(reader, $"Invalid DateOnly scalar '{reader.ScalarValue}'.");

    /// <summary>Throws an exception for invalid <see cref="TimeOnly"/> Scalar.</summary>
    public static YamlException ThrowInvalidTimeOnlyScalar(YamlReader reader)
        => ThrowInvalidScalar(reader, $"Invalid TimeOnly scalar '{reader.ScalarValue}'.");

    /// <summary>Throws an exception for invalid <see cref="Half"/> Scalar.</summary>
    public static YamlException ThrowInvalidHalfScalar(YamlReader reader)
        => ThrowInvalidScalar(reader, $"Invalid Half scalar '{reader.ScalarValue}'.");

    /// <summary>Throws an exception for invalid <see cref="Int128"/> Scalar.</summary>
    public static YamlException ThrowInvalidInt128Scalar(YamlReader reader)
        => ThrowInvalidScalar(reader, $"Invalid Int128 scalar '{reader.ScalarValue}'.");

    /// <summary>Throws an exception for invalid <see cref="UInt128"/> Scalar.</summary>
    public static YamlException ThrowInvalidUInt128Scalar(YamlReader reader)
        => ThrowInvalidScalar(reader, $"Invalid UInt128 scalar '{reader.ScalarValue}'.");

    /// <summary>Throws an exception for alias Missing Value.</summary>
    public static YamlException ThrowAliasMissingValue(YamlReader reader)
        => new(reader.SourceName, reader.Start, reader.End, "Alias token did not provide an alias value.");

    /// <summary>Throws an exception when required members are missing from an object mapping.</summary>
    /// <param name="reader">The reader positioned at the end of the mapping.</param>
    /// <param name="mappingStart">The start location of the mapping.</param>
    /// <param name="declaringType">The target CLR type being deserialized.</param>
    /// <param name="missingMemberNames">The YAML member names that were missing.</param>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/>, <paramref name="declaringType"/>, or <paramref name="missingMemberNames"/> is <see langword="null"/>.</exception>
    public static YamlException ThrowMissingRequiredMembers(YamlReader reader, Mark mappingStart, Type declaringType, IReadOnlyList<string> missingMemberNames)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(missingMemberNames);

        var joined = missingMemberNames.Count == 0 ? string.Empty : string.Join(", ", missingMemberNames);
        var message = missingMemberNames.Count == 0
            ? $"Missing required members for '{declaringType}'."
            : $"Missing required members for '{declaringType}': {joined}.";

        return new YamlException(reader.SourceName, mappingStart, reader.End, message);
    }

    /// <summary>Throws an exception when a lifecycle callback fails during deserialization.</summary>
    /// <param name="reader">The reader used for deserialization.</param>
    /// <param name="declaringType">The CLR type being deserialized.</param>
    /// <param name="callbackName">The callback name being invoked.</param>
    /// <param name="exception">The callback exception.</param>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/>, <paramref name="declaringType"/>, <paramref name="callbackName"/>, or <paramref name="exception"/> is <see langword="null"/>.</exception>
    public static YamlException ThrowCallbackInvocationFailed(YamlReader reader, Type declaringType, string callbackName, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(callbackName);
        ArgumentNullException.ThrowIfNull(exception);

        return new YamlException(reader.SourceName, reader.Start, reader.End, $"An error occurred while invoking '{callbackName}' on '{declaringType}'.", exception);
    }

    /// <summary>Throws an exception when a lifecycle callback fails during serialization.</summary>
    /// <param name="declaringType">The CLR type being serialized.</param>
    /// <param name="callbackName">The callback name being invoked.</param>
    /// <param name="exception">The callback exception.</param>
    /// <exception cref="ArgumentNullException"><paramref name="declaringType"/>, <paramref name="callbackName"/>, or <paramref name="exception"/> is <see langword="null"/>.</exception>
    public static YamlException ThrowCallbackInvocationFailed(Type declaringType, string callbackName, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(callbackName);
        ArgumentNullException.ThrowIfNull(exception);

        return new YamlException(Mark.Empty, Mark.Empty, $"An error occurred while invoking '{callbackName}' on '{declaringType}'.", exception);
    }

    /// <summary>Throws an exception when a required constructor parameter is missing during deserialization.</summary>
    /// <param name="reader">The reader positioned at the end of the mapping.</param>
    /// <param name="mappingStart">The start location of the mapping.</param>
    /// <param name="declaringType">The target CLR type being deserialized.</param>
    /// <param name="parameterName">The constructor parameter name.</param>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/>, <paramref name="declaringType"/>, or <paramref name="parameterName"/> is <see langword="null"/>.</exception>
    public static YamlException ThrowMissingRequiredConstructorParameter(YamlReader reader, Mark mappingStart, Type declaringType, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(parameterName);

        return new YamlException(reader.SourceName, mappingStart, reader.End, $"Missing required constructor parameter '{parameterName}' for '{declaringType}'.");
    }

    /// <summary>Throws an exception when a non-nullable member receives a null value during deserialization.</summary>
    /// <param name="reader">The reader positioned on the null value.</param>
    /// <param name="declaringType">The target CLR type being deserialized.</param>
    /// <param name="memberName">The member name.</param>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/>, <paramref name="declaringType"/>, or <paramref name="memberName"/> is <see langword="null"/>.</exception>
    public static YamlException ThrowNullForNonNullableMember(YamlReader reader, Type declaringType, string memberName)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(memberName);

        return new YamlException(reader.SourceName, reader.Start, reader.End, $"The YAML member '{memberName}' cannot be null because '{declaringType}' declares it as non-nullable.");
    }

    /// <summary>Throws an exception when a non-nullable constructor parameter receives a null value during deserialization.</summary>
    /// <param name="reader">The reader positioned on the null value.</param>
    /// <param name="declaringType">The target CLR type being deserialized.</param>
    /// <param name="parameterName">The constructor parameter name.</param>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/>, <paramref name="declaringType"/>, or <paramref name="parameterName"/> is <see langword="null"/>.</exception>
    public static YamlException ThrowNullForNonNullableConstructorParameter(YamlReader reader, Type declaringType, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(parameterName);

        return new YamlException(reader.SourceName, reader.Start, reader.End, $"The constructor parameter '{parameterName}' on '{declaringType}' cannot be null because it is declared as non-nullable.");
    }

    /// <summary>Throws an exception when a non-nullable member value is null during serialization.</summary>
    /// <param name="declaringType">The CLR type being serialized.</param>
    /// <param name="memberName">The member name.</param>
    /// <exception cref="ArgumentNullException"><paramref name="declaringType"/> or <paramref name="memberName"/> is <see langword="null"/>.</exception>
    public static YamlException ThrowNullForNonNullableMember(Type declaringType, string memberName)
    {
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(memberName);

        return new YamlException(Mark.Empty, Mark.Empty, $"The member '{memberName}' on '{declaringType}' cannot be serialized as null because it is declared as non-nullable.");
    }

    /// <summary>Throws an exception when an unmapped member is encountered during deserialization.</summary>
    /// <param name="reader">The reader positioned on the unmapped member value.</param>
    /// <param name="declaringType">The target CLR type being deserialized.</param>
    /// <param name="memberName">The unmapped YAML member name.</param>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/>, <paramref name="declaringType"/>, or <paramref name="memberName"/> is <see langword="null"/>.</exception>
    public static YamlException ThrowUnmappedMember(YamlReader reader, Type declaringType, string memberName)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(memberName);

        return new YamlException(reader.SourceName, reader.Start, reader.End, $"The YAML member '{memberName}' could not be mapped to '{declaringType}'.");
    }
}
