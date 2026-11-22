using System.ComponentModel;
using Meziantou.Framework.Csv.Serialization.Converters;

namespace Meziantou.Framework.Csv.Serialization.Metadata;

/// <summary>Creates serialization metadata. The code generated for a <see cref="CsvSerializerContext"/> calls these methods.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class CsvMetadataServices
{
    /// <summary>Creates the metadata of a type.</summary>
    /// <typeparam name="T">The type described.</typeparam>
    /// <param name="options">The options the metadata is created for.</param>
    /// <param name="values">The description of the type.</param>
    /// <returns>The metadata of the type.</returns>
    public static CsvTypeInfo<T> CreateObjectInfo<T>(CsvSerializerOptions options, CsvObjectInfoValues<T> values)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(values);

        var properties = new CsvPropertyInfo<T>[values.Properties.Count];
        for (var i = 0; i < properties.Length; i++)
        {
            properties[i] = values.Properties[i] as CsvPropertyInfo<T>
                ?? throw new ArgumentException($"The property '{values.Properties[i].MemberName}' is not a member of '{typeof(T)}'.", nameof(values));
        }

        // Stable sort: the members keep their declaration order when they have the same order
        var ordered = properties.Select((property, index) => (property, index)).OrderBy(item => item.property.Order).ThenBy(item => item.index).Select(item => item.property).ToArray();
        return new CsvTypeInfo<T>(options, values, ordered);
    }

    /// <summary>Creates the metadata of a column.</summary>
    /// <typeparam name="TDeclaring">The type that declares the member.</typeparam>
    /// <typeparam name="TValue">The type of the member.</typeparam>
    /// <param name="options">The options the metadata is created for.</param>
    /// <param name="values">The description of the column.</param>
    /// <returns>The metadata of the column.</returns>
    /// <exception cref="NotSupportedException">No converter converts <typeparamref name="TValue"/>.</exception>
    public static CsvPropertyInfo CreatePropertyInfo<TDeclaring, TValue>(CsvSerializerOptions options, CsvPropertyInfoValues<TDeclaring, TValue> values)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(values);

        var name = values.ColumnName ?? options.PropertyNamingPolicy?.ConvertName(values.MemberName) ?? values.MemberName;
        var converter = options.ResolveConverter(values.Converter, values.DefaultConverter, $"{typeof(TDeclaring)}.{values.MemberName}");
        var context = new CsvConverterContext(options, values.Format, values.MemberName);
        return new CsvPropertyInfo<TDeclaring, TValue>(name, values, converter, context);
    }

    /// <summary>Gets the converter of <typeparamref name="T"/>: a converter of <see cref="CsvSerializerOptions.Converters"/>, or a built-in converter.</summary>
    /// <typeparam name="T">The type of the values.</typeparam>
    /// <param name="options">The options.</param>
    /// <returns>The converter.</returns>
    /// <exception cref="NotSupportedException">No converter converts <typeparamref name="T"/>.</exception>
    public static CsvConverter<T> GetConverter<T>(CsvSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.ResolveConverter<T>(customConverter: null, defaultConverter: null, memberName: null);
    }

    /// <summary>Gets the converter of <typeparamref name="T"/>: a converter of <see cref="CsvSerializerOptions.Converters"/>, <paramref name="defaultConverter"/>, or a built-in converter.</summary>
    /// <typeparam name="T">The type of the values.</typeparam>
    /// <param name="options">The options.</param>
    /// <param name="defaultConverter">The converter to use when no converter of the options converts <typeparamref name="T"/>, or <see langword="null"/> for the built-in converters.</param>
    /// <returns>The converter.</returns>
    /// <exception cref="NotSupportedException">No converter converts <typeparamref name="T"/>.</exception>
    public static CsvConverter<T> GetConverter<T>(CsvSerializerOptions options, CsvConverter<T>? defaultConverter)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.ResolveConverter(customConverter: null, defaultConverter, memberName: null);
    }

    /// <summary>Gets a converter that reads the names and the numbers of the members of an enumeration, ignoring case, and writes their names.</summary>
    /// <typeparam name="TEnum">The type of the enumeration.</typeparam>
    /// <returns>The converter.</returns>
    public static CsvConverter<TEnum> GetEnumConverter<TEnum>()
        where TEnum : struct, Enum
    {
        return EnumConverterCache<TEnum>.Instance;
    }

    /// <summary>Gets a converter of <see cref="Nullable{T}"/>: an empty value is <see langword="null"/>, other values are converted by <paramref name="converter"/>.</summary>
    /// <typeparam name="T">The underlying type.</typeparam>
    /// <param name="converter">The converter of <typeparamref name="T"/>.</param>
    /// <returns>The converter.</returns>
    public static CsvConverter<T?> GetNullableConverter<T>(CsvConverter<T> converter)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(converter);
        return new NullableCsvConverter<T>(converter);
    }

    /// <summary>Gets a converter that parses and formats values with the <see cref="ISpanParsable{TSelf}"/> and <see cref="ISpanFormattable"/> implementations of <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The type of the values.</typeparam>
    /// <returns>The converter.</returns>
    public static CsvConverter<T> GetSpanParsableConverter<T>()
        where T : ISpanParsable<T>, ISpanFormattable
    {
        return SpanParsableConverterCache<T>.Instance;
    }

    private static class EnumConverterCache<TEnum>
        where TEnum : struct, Enum
    {
        public static readonly EnumCsvConverter<TEnum> Instance = new();
    }

    private static class SpanParsableConverterCache<T>
        where T : ISpanParsable<T>, ISpanFormattable
    {
        public static readonly SpanParsableCsvConverter<T> Instance = new();
    }
}
