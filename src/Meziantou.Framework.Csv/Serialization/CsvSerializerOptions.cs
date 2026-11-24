using System.Collections.Concurrent;
using Meziantou.Framework.Csv.Serialization.Converters;
using Meziantou.Framework.Csv.Serialization.Metadata;

namespace Meziantou.Framework.Csv.Serialization;

/// <summary>Options that control how <see cref="CsvSerializer"/> reads and writes records.</summary>
/// <remarks>
/// The options are immutable, and the metadata of the types is cached in the instance: reuse the same instance rather than creating one for each call.
/// Use <see cref="CsvSerializerOptions(CsvSerializerOptions)"/> to create a copy with other values.
/// </remarks>
public sealed class CsvSerializerOptions
{
    private readonly ConcurrentDictionary<Type, CsvTypeInfo> _typeInfos = new();
    private CsvReaderOptions? _readerOptions;
    private CsvWriterOptions? _writerOptions;

    /// <summary>Initializes a new instance of the <see cref="CsvSerializerOptions"/> class with the default values.</summary>
    public CsvSerializerOptions()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="CsvSerializerOptions"/> class with the values of other options.</summary>
    /// <param name="options">The options to copy.</param>
    public CsvSerializerOptions(CsvSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Separator = options.Separator;
        Quote = options.Quote;
        HasHeaderRow = options.HasHeaderRow;
        NewLine = options.NewLine;
        Trim = options.Trim;
        CommentCharacter = options.CommentCharacter;
        QuoteMode = options.QuoteMode;
        EscapeFormulas = options.EscapeFormulas;
        Strict = options.Strict;
        BadDataHandler = options.BadDataHandler;
        DeserializationErrorHandler = options.DeserializationErrorHandler;
        FormatProvider = options.FormatProvider;
        PropertyNamingPolicy = options.PropertyNamingPolicy;
        PropertyNameCaseInsensitive = options.PropertyNameCaseInsensitive;
        IncludeFields = options.IncludeFields;
        Converters = options.Converters;
        TypeInfoResolver = options.TypeInfoResolver;
        MissingColumnHandling = options.MissingColumnHandling;
        UnmappedColumnHandling = options.UnmappedColumnHandling;
        BufferSize = options.BufferSize;
        MaxRowLength = options.MaxRowLength;
        MaxDegreeOfParallelism = options.MaxDegreeOfParallelism;
    }

    /// <summary>Gets the default options.</summary>
    public static CsvSerializerOptions Default { get; } = new();

    /// <summary>Gets the character that separates the values of a row. Default is <c>,</c>.</summary>
    public char Separator { get; init; } = CsvReaderOptions.DefaultSeparator;

    /// <summary>Gets the character that quotes values. Default is <c>"</c>. When <see langword="null"/>, values are never quoted.</summary>
    public char? Quote { get; init; } = CsvReaderOptions.DefaultQuote;

    /// <summary>Gets a value indicating whether the first row holds the names of the columns. Default is <see langword="true"/>.</summary>
    /// <remarks>When <see langword="true"/>, the columns are read by name and the header row is written. Otherwise, the columns are read and written by position.</remarks>
    public bool HasHeaderRow { get; init; } = true;

    /// <summary>Gets the string written at the end of each row. Default is <c>\r\n</c>.</summary>
    public string NewLine { get; init; } = "\r\n";

    /// <summary>Gets which spaces are removed from the values read. Default is <see cref="CsvTrimOptions.None"/>.</summary>
    public CsvTrimOptions Trim { get; init; }

    /// <summary>Gets the character that starts a comment line, or <see langword="null"/> when the data has no comments. Default is <see langword="null"/>.</summary>
    public char? CommentCharacter { get; init; }

    /// <summary>Gets when values are quoted. Default is <see cref="CsvQuoteMode.AsNeeded"/>.</summary>
    public CsvQuoteMode QuoteMode { get; init; }

    /// <summary>Gets a value indicating whether string values that a spreadsheet would run as a formula are escaped. Default is <see langword="false"/>.</summary>
    /// <remarks>See <see cref="CsvWriterOptions.EscapeFormulas"/>.</remarks>
    public bool EscapeFormulas { get; init; }

    /// <summary>Gets a value indicating whether invalid quoting throws a <see cref="CsvException"/>. Default is <see langword="false"/>.</summary>
    /// <remarks>See <see cref="CsvReaderOptions.Strict"/>.</remarks>
    public bool Strict { get; init; }

    /// <summary>Gets the method called for malformed data, which decides whether the serializer throws, accepts the data, or skips the record. Default is <see langword="null"/>.</summary>
    /// <remarks>See <see cref="CsvReaderOptions.BadDataHandler"/>. To handle the values that cannot be converted, use <see cref="DeserializationErrorHandler"/>.</remarks>
    public CsvBadDataHandler? BadDataHandler { get; init; }

    /// <summary>Gets the method called when a record cannot be converted to an object, which decides whether the serializer throws, ignores the error, or skips the record. Default is <see langword="null"/>.</summary>
    /// <remarks>
    /// <para>
    /// The handler is called for the errors that throw a <see cref="CsvException"/> without it (see <see cref="CsvDeserializationErrorKind"/>): a value that
    /// cannot be converted, a missing value or column for a required member, and the missing and unmapped columns when <see cref="MissingColumnHandling"/>
    /// or <see cref="UnmappedColumnHandling"/> is <c>Throw</c>. When it is <see langword="null"/>, the serializer throws.
    /// </para>
    /// <para>
    /// A record can have several errors, reported one by one until the handler returns <see cref="CsvDeserializationErrorAction.SkipRecord"/>. The errors of
    /// the header row are reported when the first record is read. The exceptions thrown by the members, the constructors and the callbacks of the type are not
    /// reported. The handler may throw its own exception, which ends the reading.
    /// </para>
    /// <para>The handler is used by the reflection-based metadata and by the generated <see cref="CsvSerializerContext"/> alike.</para>
    /// </remarks>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Csv.Tests/CsvSerializerTests.cs" ref:region="CsvSerializer_DeserializationErrorHandler" ref:dedent="true">
    /// var errors = new List&lt;string&gt;();
    /// var options = new CsvSerializerOptions
    /// {
    ///     DeserializationErrorHandler = context =&gt;
    ///     {
    ///         errors.Add($"line {context.LineNumber}: '{context.RawValue}' is not a valid {context.MemberName}");
    ///         return CsvDeserializationErrorAction.SkipRecord; // or Ignore to keep the default value, or Throw
    ///     },
    /// };
    ///
    /// var products = CsvSerializer.Deserialize&lt;Product&gt;("Id,Name,Price\n1,Keyboard,49.90\nx,Mouse,19.99\n3,Screen,199", options);
    ///
    /// Assert.Equal(["Keyboard", "Screen"], products.Select(p =&gt; p.Name));
    /// Assert.Equal(["line 3: 'x' is not a valid Id"], errors);
    /// </code>
    /// </example>
    public CsvDeserializationErrorHandler? DeserializationErrorHandler { get; init; }

    /// <summary>Gets the format provider used to parse and format values. Default is <see langword="null"/>, which means <see cref="CultureInfo.InvariantCulture"/>.</summary>
    public IFormatProvider? FormatProvider { get; init; }

    /// <summary>Gets the policy that converts the names of the members to the names of the columns, or <see langword="null"/> to use the names of the members. Default is <see langword="null"/>.</summary>
    public CsvNamingPolicy? PropertyNamingPolicy { get; init; }

    /// <summary>Gets a value indicating whether the names of the columns are matched ignoring case. Default is <see langword="false"/>.</summary>
    public bool PropertyNameCaseInsensitive { get; init; }

    /// <summary>Gets a value indicating whether public fields are serialized. Default is <see langword="false"/>.</summary>
    public bool IncludeFields { get; init; }

    /// <summary>Gets the converters used before the built-in converters. Default is an empty list.</summary>
    public IReadOnlyList<CsvConverter> Converters { get; init; } = [];

    /// <summary>Gets the resolver of the metadata of the types, such as a generated <see cref="CsvSerializerContext"/>. Default is <see langword="null"/>: the metadata is created with reflection.</summary>
    public ICsvTypeInfoResolver? TypeInfoResolver { get; init; }

    /// <summary>Gets how a member without a column is handled when reading data with a header row. Default is <see cref="CsvMissingColumnHandling.Ignore"/>.</summary>
    public CsvMissingColumnHandling MissingColumnHandling { get; init; }

    /// <summary>Gets how a column without a member is handled when reading data. Default is <see cref="CsvUnmappedColumnHandling.Ignore"/>.</summary>
    public CsvUnmappedColumnHandling UnmappedColumnHandling { get; init; }

    /// <summary>Gets the size of the buffers of the readers and the writers. Default is 16384.</summary>
    public int BufferSize { get; init; } = CsvReaderOptions.DefaultBufferSize;

    /// <summary>Gets the maximum length of a row read from a <see cref="TextReader"/> or a <see cref="Stream"/>. Default is 16777216.</summary>
    public int MaxRowLength { get; init; } = CsvReaderOptions.DefaultMaxRowLength;

    /// <summary>Gets the maximum number of threads that convert the records to objects when reading data. Default is 1.</summary>
    /// <value>1 to convert the records on the calling thread, a larger value to convert them on the thread pool, or -1 to use <see cref="Environment.ProcessorCount"/>.</value>
    /// <remarks>
    /// <para>
    /// When larger than 1, the data is still split into records on the calling thread, and the records are converted to objects on the thread pool, in
    /// batches of a few hundred records; the calling thread converts the next batch itself when no thread of the pool has started it. The objects are
    /// returned in the order of the records, and the exceptions are the same as when reading the records one by one. The enumerable overloads read a
    /// few batches ahead of the objects they return. The gain is largest when converting the values takes time, such as numbers and dates, and smallest
    /// when strings are most of the values: the garbage collector limits their allocation, less so the server garbage collector. Splitting the data on
    /// one thread limits the gain to a few times, whatever the number of processors.
    /// </para>
    /// <para>
    /// The constructors, the setters, the converters and the callbacks of the records are called from several threads at the same time. The
    /// <see cref="DeserializationErrorHandler"/> is not called by two threads at the same time, but it can be called for the records in another order, and
    /// for records after the error that ends the reading. The <see cref="BadDataHandler"/> is called on the calling thread. The browser and WASI convert
    /// the records on the calling thread.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is 0 or less than -1.</exception>
    public int MaxDegreeOfParallelism
    {
        get;
        init
        {
            if (value is 0 or < -1)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The maximum degree of parallelism must be -1 or a positive number.");

            field = value;
        }
    } = 1;

    internal CsvReaderOptions ReaderOptions => _readerOptions ??= new CsvReaderOptions
    {
        Separator = Separator,
        Quote = Quote,
        HasHeaderRow = HasHeaderRow,
        Trim = Trim,
        CommentCharacter = CommentCharacter,
        SkipEmptyLines = true,
        Strict = Strict,
        BadDataHandler = BadDataHandler,
        FormatProvider = FormatProvider,
        ColumnNameComparer = PropertyNameCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal,
        BufferSize = BufferSize,
        MaxRowLength = MaxRowLength,
    };

    internal CsvWriterOptions WriterOptions => _writerOptions ??= new CsvWriterOptions
    {
        Separator = Separator,
        Quote = Quote,
        NewLine = NewLine,
        QuoteMode = QuoteMode,
        EscapeFormulas = EscapeFormulas,
        FormatProvider = FormatProvider,
        BufferSize = BufferSize,
    };

    /// <summary>Gets the metadata of <typeparamref name="T"/>, from <see cref="TypeInfoResolver"/> or created with reflection.</summary>
    /// <typeparam name="T">The type.</typeparam>
    /// <returns>The metadata of the type.</returns>
    /// <exception cref="NotSupportedException">The type is not supported.</exception>
    [RequiresUnreferencedCode(CsvSerializer.ReflectionMessage)]
    [RequiresDynamicCode(CsvSerializer.ReflectionMessage)]
    public CsvTypeInfo<T> GetTypeInfo<T>() => (CsvTypeInfo<T>)GetTypeInfo(typeof(T));

    /// <summary>Gets the metadata of a type, from <see cref="TypeInfoResolver"/> or created with reflection.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The metadata of the type.</returns>
    /// <exception cref="NotSupportedException">The type is not supported.</exception>
    [RequiresUnreferencedCode(CsvSerializer.ReflectionMessage)]
    [RequiresDynamicCode(CsvSerializer.ReflectionMessage)]
    public CsvTypeInfo GetTypeInfo(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return GetTypeInfoCore(type, allowReflection: true);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Reflection is only used when allowed by the caller, from the reflection-based APIs.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Reflection is only used when allowed by the caller, from the reflection-based APIs.")]
    internal CsvTypeInfo GetTypeInfoCore(Type type, bool allowReflection)
    {
        if (_typeInfos.TryGetValue(type, out var result))
            return result;

        CsvTypeInfo? typeInfo;
        if (TypeInfoResolver is not null)
        {
            typeInfo = TypeInfoResolver.GetTypeInfo(type, this);
        }
        else if (allowReflection && CsvSerializer.IsReflectionEnabledByDefault)
        {
            typeInfo = ReflectionCsvTypeInfoResolver.Default.GetTypeInfo(type, this);
        }
        else
        {
            throw new InvalidOperationException($"Reflection-based serialization is disabled and no metadata is available for '{type}'. Use a generated CsvSerializerContext.");
        }

        if (typeInfo is null)
            throw new NotSupportedException($"The type '{type}' is not supported by the TypeInfoResolver of the options.");

        if (typeInfo.Type != type)
            throw new InvalidOperationException($"The TypeInfoResolver returned the metadata of '{typeInfo.Type}' for '{type}'.");

        return _typeInfos.GetOrAdd(type, typeInfo);
    }

    internal CsvConverter<T> ResolveConverter<T>(CsvConverter? customConverter, CsvConverter<T>? defaultConverter, string? memberName)
    {
        var converter = customConverter;
        if (converter is null)
        {
            foreach (var candidate in Converters)
            {
                if (candidate.CanConvert(typeof(T)))
                {
                    converter = candidate;
                    break;
                }
            }
        }

        if (converter is CsvConverterFactory factory)
        {
            converter = factory.CreateConverter(typeof(T), this)
                ?? throw new InvalidOperationException($"The converter factory '{factory.GetType()}' returned null for '{typeof(T)}'.");
        }

        converter ??= defaultConverter ?? CsvDefaultConverters.Get<T>();
        if (converter is null)
            throw new NotSupportedException($"No converter converts '{typeof(T)}'{(memberName is null ? "" : $" ({memberName})")}. Add a converter to CsvSerializerOptions.Converters or use CsvConverterAttribute.");

        if (converter is not CsvConverter<T> typedConverter)
            throw new InvalidOperationException($"The converter '{converter.GetType()}' does not convert '{typeof(T)}'.");

        return typedConverter;
    }
}
