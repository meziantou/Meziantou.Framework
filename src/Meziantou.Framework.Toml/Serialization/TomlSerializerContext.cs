using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Toml;
using Meziantou.Framework.Toml.Helpers;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Serialization.Converters;
using Meziantou.Framework.Toml.Serialization.Internal;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml.Serialization;

/// <summary>
/// Base type for source-generated TOML serializer contexts.
/// </summary>
public abstract partial class TomlSerializerContext : ITomlTypeInfoResolver
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TomlSerializerContext"/> class.
    /// </summary>
    protected TomlSerializerContext()
        : this(new TomlSerializerOptions())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TomlSerializerContext"/> class.
    /// </summary>
    /// <param name="options">The options used by this context.</param>
    protected TomlSerializerContext(TomlSerializerOptions options)
    {
        ArgumentGuard.ThrowIfNull(options, nameof(options));

        if (options.TypeInfoResolver is null)
        {
            Options = options with { TypeInfoResolver = this };
            return;
        }

        if (!ReferenceEquals(options.TypeInfoResolver, this))
        {
            throw new ArgumentException(
                $"The provided {nameof(TomlSerializerOptions)} instance is associated with a different {nameof(TomlSerializerOptions.TypeInfoResolver)}. " +
                $"A {nameof(TomlSerializerContext)} must use an options instance whose {nameof(TomlSerializerOptions.TypeInfoResolver)} is the context itself.",
                nameof(options));
        }

        Options = options;
    }

    /// <summary>
    /// Gets the options instance associated with this context.
    /// </summary>
    public TomlSerializerOptions Options { get; }

    /// <inheritdoc />
    public abstract TomlTypeInfo? GetTypeInfo(Type type, TomlSerializerOptions options);

    /// <summary>
    /// Resolves converter-based metadata for a runtime converter registered on <paramref name="options" />.
    /// </summary>
    /// <param name="options">The options that may contain runtime converters.</param>
    /// <param name="type">The type to resolve.</param>
    /// <param name="ignoredConverterTypes">Converter types supplied by source-generation options that should not be treated as runtime overrides.</param>
    /// <returns>Converter-based metadata, or <see langword="null" /> when no runtime converter matches.</returns>
    protected static TomlTypeInfo? ResolveRuntimeConverterTypeInfo(TomlSerializerOptions options, Type type, Type[]? ignoredConverterTypes)
    {
        ArgumentGuard.ThrowIfNull(options, nameof(options));
        ArgumentGuard.ThrowIfNull(type, nameof(type));
        return TomlTypeInfoResolverPipeline.TryResolveFromConverters(options, type, ignoredConverterTypes);
    }

    /// <summary>
    /// Resolves built-in type metadata for a known scalar/container type.
    /// </summary>
    /// <remarks>
    /// This method exists to support source-generated contexts without requiring generated code to reference internal resolver types.
    /// </remarks>
    protected static TomlTypeInfo<T> GetBuiltInTypeInfo<T>(TomlSerializerOptions options)
    {
        ArgumentGuard.ThrowIfNull(options, nameof(options));

        var type = typeof(T);

        if (type == typeof(char)) return (TomlTypeInfo<T>)(object)new TomlConverterTypeInfo<char>(options, TomlCharConverter.Instance);
        if (type == typeof(string)) return (TomlTypeInfo<T>)(object)new TomlConverterTypeInfo<string>(options, TomlStringConverter.Instance);
        if (type == typeof(bool)) return (TomlTypeInfo<T>)(object)new TomlConverterTypeInfo<bool>(options, TomlBooleanConverter.Instance);
        if (type == typeof(sbyte)) return (TomlTypeInfo<T>)(object)new TomlConverterTypeInfo<sbyte>(options, TomlSByteConverter.Instance);
        if (type == typeof(byte)) return (TomlTypeInfo<T>)(object)new TomlConverterTypeInfo<byte>(options, TomlByteConverter.Instance);
        if (type == typeof(short)) return (TomlTypeInfo<T>)(object)new TomlConverterTypeInfo<short>(options, TomlInt16Converter.Instance);
        if (type == typeof(ushort)) return (TomlTypeInfo<T>)(object)new TomlConverterTypeInfo<ushort>(options, TomlUInt16Converter.Instance);
        if (type == typeof(int)) return (TomlTypeInfo<T>)(object)new TomlConverterTypeInfo<int>(options, TomlInt32Converter.Instance);
        if (type == typeof(uint)) return (TomlTypeInfo<T>)(object)new TomlConverterTypeInfo<uint>(options, TomlUInt32Converter.Instance);
        if (type == typeof(long)) return (TomlTypeInfo<T>)(object)new TomlConverterTypeInfo<long>(options, TomlInt64Converter.Instance);
        if (type == typeof(ulong)) return (TomlTypeInfo<T>)(object)new TomlConverterTypeInfo<ulong>(options, TomlUInt64Converter.Instance);
        if (type == typeof(nint)) return (TomlTypeInfo<T>)(object)new TomlConverterTypeInfo<nint>(options, TomlNIntConverter.Instance);
        if (type == typeof(nuint)) return (TomlTypeInfo<T>)(object)new TomlConverterTypeInfo<nuint>(options, TomlNUIntConverter.Instance);
        if (type == typeof(float)) return (TomlTypeInfo<T>)(object)new TomlConverterTypeInfo<float>(options, TomlSingleConverter.Instance);
        if (type == typeof(double)) return (TomlTypeInfo<T>)(object)new TomlConverterTypeInfo<double>(options, TomlDoubleConverter.Instance);
        if (type == typeof(decimal)) return (TomlTypeInfo<T>)(object)new TomlConverterTypeInfo<decimal>(options, TomlDecimalConverter.Instance);

        if (type == typeof(Half)) return (TomlTypeInfo<T>)(object)new TomlConverterTypeInfo<Half>(options, TomlHalfConverter.Instance);

        if (type == typeof(Int128)) return (TomlTypeInfo<T>)(object)new TomlConverterTypeInfo<Int128>(options, TomlInt128Converter.Instance);
        if (type == typeof(UInt128)) return (TomlTypeInfo<T>)(object)new TomlConverterTypeInfo<UInt128>(options, TomlUInt128Converter.Instance);

        if (type == typeof(DateTime)) return (TomlTypeInfo<T>)(object)new TomlConverterTypeInfo<DateTime>(options, TomlDateTimeConverter.Instance);
        if (type == typeof(DateTimeOffset)) return (TomlTypeInfo<T>)(object)new TomlConverterTypeInfo<DateTimeOffset>(options, TomlDateTimeOffsetConverter.Instance);
        if (type == typeof(TomlDateTime)) return (TomlTypeInfo<T>)(object)new TomlConverterTypeInfo<TomlDateTime>(options, TomlTomlDateTimeConverter.Instance);

        if (type == typeof(DateOnly)) return (TomlTypeInfo<T>)(object)new TomlConverterTypeInfo<DateOnly>(options, TomlDateOnlyConverter.Instance);
        if (type == typeof(TimeOnly)) return (TomlTypeInfo<T>)(object)new TomlConverterTypeInfo<TimeOnly>(options, TomlTimeOnlyConverter.Instance);

        if (type == typeof(Guid)) return (TomlTypeInfo<T>)(object)new TomlConverterTypeInfo<Guid>(options, TomlGuidConverter.Instance);
        if (type == typeof(TimeSpan)) return (TomlTypeInfo<T>)(object)new TomlConverterTypeInfo<TimeSpan>(options, TomlTimeSpanConverter.Instance);
        if (type == typeof(Uri)) return (TomlTypeInfo<T>)(object)new TomlConverterTypeInfo<Uri>(options, TomlUriConverter.Instance);
        if (type == typeof(Version)) return (TomlTypeInfo<T>)(object)new TomlConverterTypeInfo<Version>(options, TomlVersionConverter.Instance);

        if (type == typeof(TomlObject)) return (TomlTypeInfo<T>)(object)new TomlConverterTypeInfo<TomlObject>(options, TomlTomlObjectConverter.Instance);
        if (type == typeof(TomlTable)) return (TomlTypeInfo<T>)(object)new TomlConverterTypeInfo<TomlTable>(options, TomlTomlTableConverter.Instance, writesTable: true);
        if (type == typeof(TomlArray)) return (TomlTypeInfo<T>)(object)new TomlConverterTypeInfo<TomlArray>(options, TomlTomlArrayConverter.Instance);
        if (type == typeof(TomlTableArray)) return (TomlTypeInfo<T>)(object)new TomlConverterTypeInfo<TomlTableArray>(options, TomlTomlTableArrayConverter.Instance);

        if (type == typeof(object)) return (TomlTypeInfo<T>)(object)new TomlUntypedConverterTypeInfo<object>(options, TomlUntypedObjectConverter.Instance);
        if (type.IsEnum) return new TomlUntypedConverterTypeInfo<T>(options, TomlEnumConverter.Instance);

        throw TomlException.CreateConfigurationError($"No built-in TOML metadata is available for type '{type.FullName}'.");
    }

    /// <summary>
    /// Records a recoverable deserialization diagnostic and skips the current TOML value when the reader is still
    /// positioned on the same token that failed.
    /// </summary>
    /// <param name="reader">The TOML reader.</param>
    /// <param name="tokenType">The token type before the read attempt.</param>
    /// <param name="span">The source span before the read attempt.</param>
    /// <param name="exception">The exception raised by the read attempt.</param>
    /// <returns><c>true</c> when the diagnostic was recorded and the current value was skipped, or when the value was read and its errors were already recorded.</returns>
    protected static bool TryAddDeserializationDiagnostic(TomlReader reader, TomlTokenType tokenType, TomlSourceSpan? span, TomlException exception)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));
        ArgumentGuard.ThrowIfNull(exception, nameof(exception));

        if (reader.OperationState.IsRecordedValueError(exception))
        {
            return true;
        }

        if (reader.TokenType != tokenType || !Nullable.Equals(reader.CurrentSpan, span) || !reader.OperationState.CanAddDiagnostics(exception))
        {
            return false;
        }

        reader.OperationState.AddDiagnostics(exception);
        reader.Skip();
        return true;
    }

    /// <summary>
    /// Gets the number of recoverable deserialization diagnostics recorded for the reader, to pass to
    /// <see cref="ThrowIfDeserializationDiagnostics(TomlReader, int)"/> at the end of a table.
    /// </summary>
    /// <param name="reader">The TOML reader.</param>
    /// <returns>The number of diagnostics.</returns>
    protected static int GetDeserializationDiagnosticCount(TomlReader reader)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));

        return reader.OperationState.DiagnosticCount;
    }

    /// <summary>
    /// Throws a <see cref="TomlException"/> when recoverable deserialization diagnostics were recorded since
    /// <see cref="GetDeserializationDiagnosticCount(TomlReader)"/> returned <paramref name="diagnosticCount"/>. The table that
    /// contains the value continues with its next value, so the errors of the rest of the document are reported as well.
    /// </summary>
    /// <param name="reader">The TOML reader.</param>
    /// <param name="diagnosticCount">The number of diagnostics when the table started.</param>
    protected static void ThrowIfDeserializationDiagnostics(TomlReader reader, int diagnosticCount)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));

        reader.OperationState.ThrowIfDiagnosticsSince(diagnosticCount);
    }

    /// <summary>
    /// Records an error found after a table was read, such as a missing required key, with the other deserialization
    /// diagnostics.
    /// </summary>
    /// <param name="reader">The TOML reader.</param>
    /// <param name="span">The location of the error.</param>
    /// <param name="message">The error message.</param>
    /// <returns>The exception to throw.</returns>
    protected static TomlException CreateDeserializationException(TomlReader reader, TomlSourceSpan? span, string message)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));

        return span is { } locatedSpan ? reader.OperationState.RecordValueError(new TomlException(locatedSpan, message)) : new TomlException(message);
    }

    /// <summary>
    /// Throws a <see cref="TomlException"/> when recoverable deserialization diagnostics were recorded for the reader.
    /// </summary>
    /// <param name="reader">The TOML reader.</param>
    protected static void ThrowIfDeserializationDiagnostics(TomlReader reader)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));

        if (reader.OperationState.Diagnostics is { Count: > 0 } diagnostics)
        {
            throw new TomlException(diagnostics);
        }
    }

    /// <summary>
    /// Writes a TOML property name using an optional dotted-key handling override.
    /// </summary>
    /// <param name="writer">The TOML writer.</param>
    /// <param name="name">The property name.</param>
    /// <param name="dottedKeyHandling">The dotted-key handling override, or <see langword="null" /> to use the writer options.</param>
    protected static void WritePropertyName(TomlWriter writer, string name, TomlDottedKeyHandling? dottedKeyHandling)
    {
        ArgumentGuard.ThrowIfNull(writer, nameof(writer));
        writer.WritePropertyName(name, dottedKeyHandling);
    }

    /// <summary>
    /// Applies formatting metadata for a TOML property currently being written by source-generated metadata.
    /// </summary>
    /// <param name="writer">The TOML writer.</param>
    /// <param name="name">The property name.</param>
    /// <param name="metadata">The metadata to merge.</param>
    protected static void ApplyPropertyMetadata(TomlWriter writer, string name, TomlPropertyMetadata metadata)
    {
        ArgumentGuard.ThrowIfNull(writer, nameof(writer));
        ArgumentGuard.ThrowIfNull(metadata, nameof(metadata));
        writer.ApplyPropertyMetadata(name, metadata);
    }

    /// <summary>
    /// Creates metadata for an enum type that writes values as strings.
    /// </summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    /// <param name="options">The serializer options.</param>
    /// <returns>The enum metadata.</returns>
    protected static TomlTypeInfo<TEnum> CreateStringEnumTypeInfo<TEnum>(TomlSerializerOptions options)
        where TEnum : struct, Enum
    {
        ArgumentGuard.ThrowIfNull(options, nameof(options));
        return new TomlUntypedConverterTypeInfo<TEnum>(options, TomlStringEnumConverter.Instance);
    }

    /// <summary>
    /// Starts recording how the properties of the table at the current position are written, when
    /// <see cref="TomlSerializerOptions.MetadataStore"/> is set.
    /// </summary>
    /// <typeparam name="T">The type read from the table.</typeparam>
    /// <param name="reader">The reader, positioned on <see cref="TomlTokenType.StartTable"/>.</param>
    /// <returns>The metadata to pass to <see cref="EndPropertiesMetadata{T}(TomlReader, TomlPropertiesMetadata?, T)"/>, or <see langword="null"/>.</returns>
    protected static TomlPropertiesMetadata? BeginPropertiesMetadata<T>(TomlReader reader)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));

        // The store associates metadata with an instance, which a value type does not have
        return typeof(T).IsValueType ? null : reader.BeginPropertiesMetadataCapture();
    }

    /// <summary>
    /// Stores the metadata recorded since <see cref="BeginPropertiesMetadata{T}(TomlReader)"/> for an instance.
    /// </summary>
    /// <typeparam name="T">The type read from the table.</typeparam>
    /// <param name="reader">The reader.</param>
    /// <param name="metadata">The metadata returned by <see cref="BeginPropertiesMetadata{T}(TomlReader)"/>.</param>
    /// <param name="value">The instance read from the table.</param>
    protected static void EndPropertiesMetadata<T>(TomlReader reader, TomlPropertiesMetadata? metadata, T value)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));
        if (metadata is not null && value is not null)
        {
            reader.EndPropertiesMetadataCapture(metadata, value);
        }
    }

    /// <summary>
    /// Writes the metadata stored for an instance, such as its comments, when <see cref="TomlSerializerOptions.MetadataStore"/> is set.
    /// </summary>
    /// <typeparam name="T">The type written as a table.</typeparam>
    /// <param name="writer">The writer, after the start of the table.</param>
    /// <param name="value">The instance.</param>
    protected static void AttachPropertiesMetadata<T>(TomlWriter writer, T value)
    {
        ArgumentGuard.ThrowIfNull(writer, nameof(writer));
        if (!typeof(T).IsValueType && value is not null)
        {
            writer.TryAttachMetadata(value);
        }
    }

    /// <summary>
    /// Creates metadata for a nullable enum type that writes values as strings.
    /// </summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    /// <param name="options">The serializer options.</param>
    /// <returns>The nullable enum metadata.</returns>
    protected static TomlTypeInfo<TEnum?> CreateNullableStringEnumTypeInfo<TEnum>(TomlSerializerOptions options)
        where TEnum : struct, Enum
    {
        ArgumentGuard.ThrowIfNull(options, nameof(options));
        return new TomlUntypedConverterTypeInfo<TEnum?>(options, TomlTypeInfoResolverPipeline.ResolveAttributeConverter(TomlStringEnumConverter.Instance, typeof(TEnum?), options));
    }

    /// <summary>
    /// Creates metadata for a type handled by a source-generation converter option.
    /// </summary>
    /// <typeparam name="T">The type handled by the converter.</typeparam>
    /// <param name="options">The serializer options.</param>
    /// <param name="converter">The converter instance.</param>
    /// <returns>Converter-based TOML metadata.</returns>
    protected static TomlTypeInfo<T> CreateConverterTypeInfo<T>(TomlSerializerOptions options, TomlConverter<T> converter)
    {
        ArgumentGuard.ThrowIfNull(options, nameof(options));
        ArgumentGuard.ThrowIfNull(converter, nameof(converter));
        return new TomlConverterTypeInfo<T>(options, converter);
    }

    /// <summary>
    /// Creates metadata for a converter declared with <see cref="TomlConverterAttribute"/> or <c>JsonConverterAttribute</c>.
    /// </summary>
    /// <typeparam name="T">The type handled by the converter.</typeparam>
    /// <param name="options">The serializer options.</param>
    /// <param name="converter">The converter instance, which can be a <see cref="TomlConverterFactory"/>.</param>
    /// <returns>Converter-based TOML metadata.</returns>
    /// <exception cref="TomlException">The converter cannot convert <typeparamref name="T"/>.</exception>
    protected static TomlTypeInfo<T> CreateAttributeConverterTypeInfo<T>(TomlSerializerOptions options, TomlConverter converter)
    {
        ArgumentGuard.ThrowIfNull(options, nameof(options));
        ArgumentGuard.ThrowIfNull(converter, nameof(converter));
        var resolved = TomlTypeInfoResolverPipeline.ResolveAttributeConverter(converter, typeof(T), options);
        return resolved is TomlConverter<T> typedConverter
            ? new TomlConverterTypeInfo<T>(options, typedConverter)
            : new TomlUntypedConverterTypeInfo<T>(options, resolved);
    }

    /// <summary>
    /// Determines whether an existing collection can be populated for source-generated <see cref="TomlSingleOrArrayAttribute"/> handling.
    /// </summary>
    /// <typeparam name="T">The collection element type.</typeparam>
    /// <param name="existingValue">The existing collection instance.</param>
    /// <returns><c>true</c> when the collection can be populated in place.</returns>
    protected static bool CanPopulateSingleOrArrayCollection<T>(object existingValue)
        => TomlSingleOrArrayCollectionHelper.CanPopulateCollection<T>(existingValue);

    /// <summary>
    /// Adds a single element to an existing collection for source-generated <see cref="TomlSingleOrArrayAttribute"/> handling.
    /// </summary>
    /// <typeparam name="T">The collection element type.</typeparam>
    /// <param name="existingValue">The existing collection instance.</param>
    /// <param name="element">The element to add.</param>
    /// <returns>The populated collection instance.</returns>
    protected static object AddSingleElementToSingleOrArrayCollection<T>(object existingValue, T element)
        => TomlSingleOrArrayCollectionHelper.AddSingleElementToCollection(existingValue, element);

    /// <summary>
    /// Adds incoming values to an existing collection for source-generated <see cref="TomlSingleOrArrayAttribute"/> handling.
    /// </summary>
    /// <typeparam name="T">The collection element type.</typeparam>
    /// <param name="existingValue">The existing collection instance.</param>
    /// <param name="incomingCollection">The values to add.</param>
    /// <returns>The populated collection instance.</returns>
    protected static object AddCollectionToSingleOrArrayCollection<T>(object existingValue, IEnumerable<T> incomingCollection)
        => TomlSingleOrArrayCollectionHelper.AddCollectionToExisting(existingValue, incomingCollection);

    /// <summary>
    /// Creates an array with a single element for source-generated <see cref="TomlSingleOrArrayAttribute"/> handling.
    /// </summary>
    /// <typeparam name="T">The collection element type.</typeparam>
    /// <param name="element">The element to place in the collection.</param>
    /// <returns>An array containing <paramref name="element"/>.</returns>
    protected static T[] CreateSingleElementArray<T>(T element) => TomlSingleOrArrayCollectionHelper.CreateSingleElementArray(element);

    /// <summary>
    /// Creates a list with a single element for source-generated <see cref="TomlSingleOrArrayAttribute"/> handling.
    /// </summary>
    /// <typeparam name="T">The collection element type.</typeparam>
    /// <param name="element">The element to place in the collection.</param>
    /// <returns>A list containing <paramref name="element"/>.</returns>
#pragma warning disable CA1002 // Called by the generated code, which needs a List<T>
    protected static List<T> CreateSingleElementList<T>(T element) => TomlSingleOrArrayCollectionHelper.CreateSingleElementList(element);
#pragma warning restore CA1002

    /// <summary>
    /// Creates a mutable collection with a single element for source-generated <see cref="TomlSingleOrArrayAttribute"/> handling.
    /// </summary>
    /// <typeparam name="TCollection">The collection type.</typeparam>
    /// <typeparam name="TElement">The collection element type.</typeparam>
    /// <param name="element">The element to place in the collection.</param>
    /// <returns>A collection containing <paramref name="element"/>.</returns>
    protected static TCollection CreateSingleElementCollection<TCollection, TElement>(TElement element)
        where TCollection : ICollection<TElement>, new()
        => TomlSingleOrArrayCollectionHelper.CreateSingleElementCollection<TCollection, TElement>(element);

    /// <summary>
    /// Creates a hash set with a single element for source-generated <see cref="TomlSingleOrArrayAttribute"/> handling.
    /// </summary>
    /// <typeparam name="T">The collection element type.</typeparam>
    /// <param name="element">The element to place in the collection.</param>
    /// <returns>A hash set containing <paramref name="element"/>.</returns>
    protected static HashSet<T> CreateSingleElementHashSet<T>(T element) => TomlSingleOrArrayCollectionHelper.CreateSingleElementHashSet(element);

    /// <summary>
    /// Creates an immutable array with a single element for source-generated <see cref="TomlSingleOrArrayAttribute"/> handling.
    /// </summary>
    /// <typeparam name="T">The collection element type.</typeparam>
    /// <param name="element">The element to place in the collection.</param>
    /// <returns>An immutable array containing <paramref name="element"/>.</returns>
    protected static ImmutableArray<T> CreateSingleElementImmutableArray<T>(T element) => TomlSingleOrArrayCollectionHelper.CreateSingleElementImmutableArray(element);

    /// <summary>
    /// Creates an immutable list with a single element for source-generated <see cref="TomlSingleOrArrayAttribute"/> handling.
    /// </summary>
    /// <typeparam name="T">The collection element type.</typeparam>
    /// <param name="element">The element to place in the collection.</param>
    /// <returns>An immutable list containing <paramref name="element"/>.</returns>
    protected static ImmutableList<T> CreateSingleElementImmutableList<T>(T element) => TomlSingleOrArrayCollectionHelper.CreateSingleElementImmutableList(element);

    /// <summary>
    /// Creates an immutable hash set with a single element for source-generated <see cref="TomlSingleOrArrayAttribute"/> handling.
    /// </summary>
    /// <typeparam name="T">The collection element type.</typeparam>
    /// <param name="element">The element to place in the collection.</param>
    /// <returns>An immutable hash set containing <paramref name="element"/>.</returns>
    protected static ImmutableHashSet<T> CreateSingleElementImmutableHashSet<T>(T element) => TomlSingleOrArrayCollectionHelper.CreateSingleElementImmutableHashSet(element);

    /// <summary>
    /// Creates metadata for a nullable value type (<see cref="Nullable{T}"/>) using source-generated resolution for the underlying value type.
    /// </summary>
    /// <remarks>
    /// This method avoids reflection-based metadata resolution, making it compatible with trimming and NativeAOT.
    /// </remarks>
    protected static TomlTypeInfo<T?> CreateSourceGeneratedNullableTypeInfo<T>(TomlSerializerContext context, TomlSerializerOptions? options = null)
        where T : struct
    {
        ArgumentGuard.ThrowIfNull(context, nameof(context));
        options ??= context.Options;

        var inner = context.GetTypeInfo(typeof(T), options);
        if (inner is null)
        {
            throw TomlException.CreateConfigurationError($"No TOML metadata is available for type '{typeof(T).FullName}'.");
        }

        return inner is TomlTypeInfo<T>
            ? new TomlNullableTypeInfo<T>(options, inner)
            : new TomlNullableTypeInfoWithUntypedInner<T>(options, inner);
    }

    /// <summary>
    /// Creates metadata for a single-dimensional array type.
    /// </summary>
    [RequiresUnreferencedCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
    [RequiresDynamicCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
    protected static TomlTypeInfo<TElement[]> CreateArrayTypeInfo<TElement>(TomlSerializerContext context)
    {
        ArgumentGuard.ThrowIfNull(context, nameof(context));
        return new TomlArrayTypeInfo<TElement>(context.Options);
    }

    /// <summary>
    /// Creates metadata for a single-dimensional array type using source-generated resolution for nested elements.
    /// </summary>
    /// <remarks>
    /// This method avoids reflection-based metadata resolution, making it compatible with trimming and NativeAOT.
    /// </remarks>
    protected static TomlTypeInfo<TElement[]> CreateSourceGeneratedArrayTypeInfo<TElement>(TomlSerializerContext context, TomlSerializerOptions? options = null)
    {
        ArgumentGuard.ThrowIfNull(context, nameof(context));
        return new TomlSourceGeneratedArrayTypeInfo<TElement>(context, options);
    }

    /// <summary>
    /// Creates metadata for a <see cref="List{T}"/>.
    /// </summary>
    [RequiresUnreferencedCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
    [RequiresDynamicCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
    protected static TomlTypeInfo<List<TElement>> CreateListTypeInfo<TElement>(TomlSerializerContext context)
    {
        ArgumentGuard.ThrowIfNull(context, nameof(context));
        return new TomlListTypeInfo<TElement>(context.Options);
    }

    /// <summary>
    /// Creates metadata for a <see cref="List{T}"/> using source-generated resolution for nested elements.
    /// </summary>
    /// <remarks>
    /// This method avoids reflection-based metadata resolution, making it compatible with trimming and NativeAOT.
    /// </remarks>
    protected static TomlTypeInfo<List<TElement>> CreateSourceGeneratedListTypeInfo<TElement>(TomlSerializerContext context, TomlSerializerOptions? options = null)
    {
        ArgumentGuard.ThrowIfNull(context, nameof(context));
        return new TomlSourceGeneratedListTypeInfo<TElement>(context, options);
    }

    /// <summary>
    /// Creates metadata for a <see cref="HashSet{T}"/>.
    /// </summary>
    [RequiresUnreferencedCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
    [RequiresDynamicCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
    protected static TomlTypeInfo<HashSet<TElement>> CreateHashSetTypeInfo<TElement>(TomlSerializerContext context)
    {
        ArgumentGuard.ThrowIfNull(context, nameof(context));
        return new TomlHashSetTypeInfo<TElement>(context.Options);
    }

    /// <summary>
    /// Creates metadata for a <see cref="HashSet{T}"/> using source-generated resolution for nested elements.
    /// </summary>
    /// <remarks>
    /// This method avoids reflection-based metadata resolution, making it compatible with trimming and NativeAOT.
    /// </remarks>
    protected static TomlTypeInfo<HashSet<TElement>> CreateSourceGeneratedHashSetTypeInfo<TElement>(TomlSerializerContext context, TomlSerializerOptions? options = null)
    {
        ArgumentGuard.ThrowIfNull(context, nameof(context));
        return new TomlSourceGeneratedHashSetTypeInfo<TElement>(context, options);
    }

    /// <summary>
    /// Creates metadata for a set-like interface type backed by <see cref="HashSet{T}"/>.
    /// </summary>
    [RequiresUnreferencedCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
    [RequiresDynamicCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
    protected static TomlTypeInfo<TEnumerable> CreateHashSetBackedEnumerableTypeInfo<TEnumerable, TElement>(TomlSerializerContext context)
        where TEnumerable : IEnumerable<TElement>
    {
        ArgumentGuard.ThrowIfNull(context, nameof(context));
        return new TomlHashSetBackedEnumerableTypeInfo<TEnumerable, TElement>(context.Options);
    }

    /// <summary>
    /// Creates metadata for a set-like interface type backed by <see cref="HashSet{T}"/> using source-generated resolution for nested elements.
    /// </summary>
    /// <remarks>
    /// This method avoids reflection-based metadata resolution, making it compatible with trimming and NativeAOT.
    /// </remarks>
    protected static TomlTypeInfo<TEnumerable> CreateSourceGeneratedHashSetBackedEnumerableTypeInfo<TEnumerable, TElement>(TomlSerializerContext context, TomlSerializerOptions? options = null)
        where TEnumerable : IEnumerable<TElement>
    {
        ArgumentGuard.ThrowIfNull(context, nameof(context));
        return new TomlSourceGeneratedHashSetBackedEnumerableTypeInfo<TEnumerable, TElement>(context, options);
    }

    /// <summary>
    /// Creates metadata for <see cref="ImmutableArray{T}"/>.
    /// </summary>
    [RequiresUnreferencedCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
    [RequiresDynamicCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
    protected static TomlTypeInfo<ImmutableArray<TElement>> CreateImmutableArrayTypeInfo<TElement>(TomlSerializerContext context)
    {
        ArgumentGuard.ThrowIfNull(context, nameof(context));
        return new TomlImmutableArrayTypeInfo<TElement>(context.Options);
    }

    /// <summary>
    /// Creates metadata for <see cref="ImmutableArray{T}"/> using source-generated resolution for nested elements.
    /// </summary>
    /// <remarks>
    /// This method avoids reflection-based metadata resolution, making it compatible with trimming and NativeAOT.
    /// </remarks>
    protected static TomlTypeInfo<ImmutableArray<TElement>> CreateSourceGeneratedImmutableArrayTypeInfo<TElement>(TomlSerializerContext context, TomlSerializerOptions? options = null)
    {
        ArgumentGuard.ThrowIfNull(context, nameof(context));
        return new TomlSourceGeneratedImmutableArrayTypeInfo<TElement>(context, options);
    }

    /// <summary>
    /// Creates metadata for <see cref="ImmutableList{T}"/>.
    /// </summary>
    [RequiresUnreferencedCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
    [RequiresDynamicCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
    protected static TomlTypeInfo<ImmutableList<TElement>> CreateImmutableListTypeInfo<TElement>(TomlSerializerContext context)
    {
        ArgumentGuard.ThrowIfNull(context, nameof(context));
        return new TomlImmutableListTypeInfo<TElement>(context.Options);
    }

    /// <summary>
    /// Creates metadata for <see cref="ImmutableList{T}"/> using source-generated resolution for nested elements.
    /// </summary>
    /// <remarks>
    /// This method avoids reflection-based metadata resolution, making it compatible with trimming and NativeAOT.
    /// </remarks>
    protected static TomlTypeInfo<ImmutableList<TElement>> CreateSourceGeneratedImmutableListTypeInfo<TElement>(TomlSerializerContext context, TomlSerializerOptions? options = null)
    {
        ArgumentGuard.ThrowIfNull(context, nameof(context));
        return new TomlSourceGeneratedImmutableListTypeInfo<TElement>(context, options);
    }

    /// <summary>
    /// Creates metadata for <see cref="ImmutableHashSet{T}"/>.
    /// </summary>
    [RequiresUnreferencedCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
    [RequiresDynamicCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
    protected static TomlTypeInfo<ImmutableHashSet<TElement>> CreateImmutableHashSetTypeInfo<TElement>(TomlSerializerContext context)
    {
        ArgumentGuard.ThrowIfNull(context, nameof(context));
        return new TomlImmutableHashSetTypeInfo<TElement>(context.Options);
    }

    /// <summary>
    /// Creates metadata for <see cref="ImmutableHashSet{T}"/> using source-generated resolution for nested elements.
    /// </summary>
    /// <remarks>
    /// This method avoids reflection-based metadata resolution, making it compatible with trimming and NativeAOT.
    /// </remarks>
    protected static TomlTypeInfo<ImmutableHashSet<TElement>> CreateSourceGeneratedImmutableHashSetTypeInfo<TElement>(TomlSerializerContext context, TomlSerializerOptions? options = null)
    {
        ArgumentGuard.ThrowIfNull(context, nameof(context));
        return new TomlSourceGeneratedImmutableHashSetTypeInfo<TElement>(context, options);
    }

    /// <summary>
    /// Creates metadata for an enumerable interface type backed by <see cref="List{T}"/>.
    /// </summary>
    [RequiresUnreferencedCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
    [RequiresDynamicCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
    protected static TomlTypeInfo<TEnumerable> CreateListBackedEnumerableTypeInfo<TEnumerable, TElement>(TomlSerializerContext context)
        where TEnumerable : IEnumerable<TElement>
    {
        ArgumentGuard.ThrowIfNull(context, nameof(context));
        return new TomlListBackedEnumerableTypeInfo<TEnumerable, TElement>(context.Options);
    }

    /// <summary>
    /// Creates metadata for an enumerable interface type backed by <see cref="List{T}"/> using source-generated resolution for nested elements.
    /// </summary>
    /// <remarks>
    /// This method avoids reflection-based metadata resolution, making it compatible with trimming and NativeAOT.
    /// </remarks>
    protected static TomlTypeInfo<TEnumerable> CreateSourceGeneratedListBackedEnumerableTypeInfo<TEnumerable, TElement>(TomlSerializerContext context, TomlSerializerOptions? options = null)
        where TEnumerable : IEnumerable<TElement>
    {
        ArgumentGuard.ThrowIfNull(context, nameof(context));
        return new TomlSourceGeneratedListBackedEnumerableTypeInfo<TEnumerable, TElement>(context, options);
    }

    /// <summary>
    /// Creates metadata for a mutable collection type using source-generated resolution for nested elements.
    /// </summary>
    /// <remarks>
    /// This method avoids reflection-based metadata resolution, making it compatible with trimming and NativeAOT.
    /// </remarks>
    protected static TomlTypeInfo<TCollection> CreateSourceGeneratedMutableCollectionTypeInfo<TCollection, TElement>(TomlSerializerContext context, TomlSerializerOptions? options = null)
        where TCollection : ICollection<TElement>, new()
    {
        ArgumentGuard.ThrowIfNull(context, nameof(context));
        return new TomlSourceGeneratedMutableCollectionTypeInfo<TCollection, TElement>(context, options);
    }

    /// <summary>
    /// Creates metadata for a dictionary-like type with string keys.
    /// </summary>
    [RequiresUnreferencedCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
    [RequiresDynamicCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
    protected static TomlTypeInfo<TDictionary> CreateDictionaryTypeInfo<TDictionary, TValue>(TomlSerializerContext context)
        where TDictionary : IEnumerable<KeyValuePair<string, TValue>>
    {
        ArgumentGuard.ThrowIfNull(context, nameof(context));
        return new TomlDictionaryTypeInfo<TDictionary, TValue>(context.Options);
    }

    /// <summary>
    /// Creates metadata for a dictionary-like type with string keys using source-generated resolution for nested values.
    /// </summary>
    /// <remarks>
    /// This method avoids reflection-based metadata resolution, making it compatible with trimming and NativeAOT.
    /// </remarks>
    protected static TomlTypeInfo<TDictionary> CreateSourceGeneratedDictionaryTypeInfo<TDictionary, TValue>(TomlSerializerContext context, TomlSerializerOptions? options = null)
        where TDictionary : IEnumerable<KeyValuePair<string, TValue>>
    {
        ArgumentGuard.ThrowIfNull(context, nameof(context));
        return new TomlSourceGeneratedDictionaryTypeInfo<TDictionary, TValue>(context, options);
    }

    /// <summary>
    /// Creates metadata for a concrete dictionary type with string keys, such as <see cref="SortedDictionary{TKey, TValue}"/>,
    /// using source-generated resolution for nested values.
    /// </summary>
    /// <remarks>
    /// This method avoids reflection-based metadata resolution, making it compatible with trimming and NativeAOT.
    /// </remarks>
    protected static TomlTypeInfo<TDictionary> CreateSourceGeneratedConcreteDictionaryTypeInfo<TDictionary, TValue>(TomlSerializerContext context, TomlSerializerOptions? options = null)
        where TDictionary : IDictionary<string, TValue>, new()
    {
        ArgumentGuard.ThrowIfNull(context, nameof(context));
        return new TomlSourceGeneratedDictionaryTypeInfo<TDictionary, TValue>(context, options, static () => new TDictionary());
    }
}
