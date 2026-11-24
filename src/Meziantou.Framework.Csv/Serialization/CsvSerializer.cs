using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using Meziantou.Framework.Csv.Internals;
using Meziantou.Framework.Csv.Serialization.Internals;
using Meziantou.Framework.Csv.Serialization.Metadata;

namespace Meziantou.Framework.Csv.Serialization;

/// <summary>Reads CSV data as a list of objects, and writes objects as CSV data. Each record is an object; each column is a property or a field.</summary>
/// <remarks>
/// <para>
/// The overloads that take a <see cref="CsvSerializerOptions"/> create the metadata of the type with reflection. They are not compatible with
/// trimming and Native AOT: use the overloads that take a <see cref="CsvTypeInfo{T}"/> from a generated <see cref="CsvSerializerContext"/> instead.
/// </para>
/// <para>The overloads that take a <see cref="Stream"/> read and write UTF-8 data without converting it to UTF-16.</para>
/// </remarks>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Csv.Tests/CsvSerializerTests.cs" ref:region="CsvSerializer_RoundTrip" ref:dedent="true">
/// var people = new List&lt;Person&gt;
/// {
///     new() { Name = "Alice", Age = 30, BirthDate = new DateOnly(1995, 4, 12) },
///     new() { Name = "Bob, Jr.", Age = 25 },
/// };
///
/// var csv = CsvSerializer.Serialize(people);
/// Assert.Equal("Name,Age,BirthDate\r\nAlice,30,1995-04-12\r\n\"Bob, Jr.\",25,\r\n", csv);
///
/// var result = CsvSerializer.Deserialize&lt;Person&gt;(csv);
/// Assert.Equal("Bob, Jr.", result[1].Name);
/// Assert.Null(result[1].BirthDate); // An empty value is null for a nullable type
/// </code>
/// </example>
[SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "The records are returned in a list the caller owns, without copying them")]
public static class CsvSerializer
{
    internal const string ReflectionMessage = "Reflection-based CSV serialization is not compatible with trimming and Native AOT. Use a generated CsvSerializerContext and the overloads that take a CsvTypeInfo.";

    /// <summary>Gets a value indicating whether the overloads that take a <see cref="CsvSerializerOptions"/> can create metadata with reflection.</summary>
    /// <remarks>
    /// This is a feature switch, disabled when publishing with Native AOT. Set the <c>MeziantouFrameworkCsvIsReflectionEnabledByDefault</c> MSBuild property to
    /// override it. When disabled, the trimmer removes the reflection-based code.
    /// </remarks>
    [FeatureSwitchDefinition(CsvSerializerFeatureSwitches.ReflectionSwitchName)]
    public static bool IsReflectionEnabledByDefault { get; } = CsvSerializerFeatureSwitches.IsReflectionEnabledByDefault;

    /// <summary>Reads the records of CSV data.</summary>
    /// <typeparam name="T">The type of the records.</typeparam>
    /// <param name="csv">The CSV data.</param>
    /// <param name="options">The options, or <see langword="null"/> to use <see cref="CsvSerializerOptions.Default"/>.</param>
    /// <returns>The records.</returns>
    /// <exception cref="CsvException">The data is invalid or a value cannot be converted.</exception>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static List<T> Deserialize<T>(string csv, CsvSerializerOptions? options = null) => Deserialize(csv, GetTypeInfo<T>(options));

    /// <summary>Reads the records of CSV data.</summary>
    /// <typeparam name="T">The type of the records.</typeparam>
    /// <param name="csv">The CSV data.</param>
    /// <param name="typeInfo">The metadata of <typeparamref name="T"/>.</param>
    /// <returns>The records.</returns>
    /// <exception cref="CsvException">The data is invalid or a value cannot be converted.</exception>
    public static List<T> Deserialize<T>(string csv, CsvTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(csv);
        ArgumentNullException.ThrowIfNull(typeInfo);
        using var parser = new CsvParser<char>(typeInfo.Options.ReaderOptions, source: null, csv.AsMemory());
        return ReadAll(parser, typeInfo);
    }

    /// <summary>Reads the records of UTF-8 CSV data.</summary>
    /// <typeparam name="T">The type of the records.</typeparam>
    /// <param name="utf8Csv">The UTF-8 encoded CSV data.</param>
    /// <param name="options">The options, or <see langword="null"/> to use <see cref="CsvSerializerOptions.Default"/>.</param>
    /// <returns>The records.</returns>
    /// <exception cref="CsvException">The data is invalid or a value cannot be converted.</exception>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static List<T> Deserialize<T>(ReadOnlyMemory<byte> utf8Csv, CsvSerializerOptions? options = null) => Deserialize(utf8Csv, GetTypeInfo<T>(options));

    /// <summary>Reads the records of UTF-8 CSV data.</summary>
    /// <typeparam name="T">The type of the records.</typeparam>
    /// <param name="utf8Csv">The UTF-8 encoded CSV data.</param>
    /// <param name="typeInfo">The metadata of <typeparamref name="T"/>.</param>
    /// <returns>The records.</returns>
    /// <exception cref="CsvException">The data is invalid or a value cannot be converted.</exception>
    public static List<T> Deserialize<T>(ReadOnlyMemory<byte> utf8Csv, CsvTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        using var parser = new CsvParser<byte>(typeInfo.Options.ReaderOptions, source: null, utf8Csv);
        return ReadAll(parser, typeInfo);
    }

    /// <summary>Reads the records of CSV data from a <see cref="TextReader"/>.</summary>
    /// <typeparam name="T">The type of the records.</typeparam>
    /// <param name="reader">The reader to read the data from. It is not disposed.</param>
    /// <param name="options">The options, or <see langword="null"/> to use <see cref="CsvSerializerOptions.Default"/>.</param>
    /// <returns>The records.</returns>
    /// <exception cref="CsvException">The data is invalid or a value cannot be converted.</exception>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static List<T> Deserialize<T>(TextReader reader, CsvSerializerOptions? options = null) => Deserialize(reader, GetTypeInfo<T>(options));

    /// <summary>Reads the records of CSV data from a <see cref="TextReader"/>.</summary>
    /// <typeparam name="T">The type of the records.</typeparam>
    /// <param name="reader">The reader to read the data from. It is not disposed.</param>
    /// <param name="typeInfo">The metadata of <typeparamref name="T"/>.</param>
    /// <returns>The records.</returns>
    /// <exception cref="CsvException">The data is invalid or a value cannot be converted.</exception>
    public static List<T> Deserialize<T>(TextReader reader, CsvTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(typeInfo);
        using var parser = CreateParser(reader, typeInfo);
        return ReadAll(parser, typeInfo);
    }

    /// <summary>Reads the records of UTF-8 CSV data from a <see cref="Stream"/>.</summary>
    /// <typeparam name="T">The type of the records.</typeparam>
    /// <param name="utf8Stream">The stream to read the UTF-8 data from. It is not disposed.</param>
    /// <param name="options">The options, or <see langword="null"/> to use <see cref="CsvSerializerOptions.Default"/>.</param>
    /// <returns>The records.</returns>
    /// <exception cref="CsvException">The data is invalid or a value cannot be converted.</exception>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static List<T> Deserialize<T>(Stream utf8Stream, CsvSerializerOptions? options = null) => Deserialize(utf8Stream, GetTypeInfo<T>(options));

    /// <summary>Reads the records of UTF-8 CSV data from a <see cref="Stream"/>.</summary>
    /// <typeparam name="T">The type of the records.</typeparam>
    /// <param name="utf8Stream">The stream to read the UTF-8 data from. It is not disposed.</param>
    /// <param name="typeInfo">The metadata of <typeparamref name="T"/>.</param>
    /// <returns>The records.</returns>
    /// <exception cref="CsvException">The data is invalid or a value cannot be converted.</exception>
    public static List<T> Deserialize<T>(Stream utf8Stream, CsvTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(utf8Stream);
        ArgumentNullException.ThrowIfNull(typeInfo);
        using var parser = CreateParser(utf8Stream, typeInfo);
        return ReadAll(parser, typeInfo);
    }

    /// <summary>Reads the records of UTF-8 CSV data from a <see cref="Stream"/> asynchronously.</summary>
    /// <typeparam name="T">The type of the records.</typeparam>
    /// <param name="utf8Stream">The stream to read the UTF-8 data from. It is not disposed.</param>
    /// <param name="options">The options, or <see langword="null"/> to use <see cref="CsvSerializerOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The records.</returns>
    /// <exception cref="CsvException">The data is invalid or a value cannot be converted.</exception>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static Task<List<T>> DeserializeAsync<T>(Stream utf8Stream, CsvSerializerOptions? options = null, CancellationToken cancellationToken = default) => DeserializeAsync(utf8Stream, GetTypeInfo<T>(options), cancellationToken);

    /// <summary>Reads the records of UTF-8 CSV data from a <see cref="Stream"/> asynchronously.</summary>
    /// <typeparam name="T">The type of the records.</typeparam>
    /// <param name="utf8Stream">The stream to read the UTF-8 data from. It is not disposed.</param>
    /// <param name="typeInfo">The metadata of <typeparamref name="T"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The records.</returns>
    /// <exception cref="CsvException">The data is invalid or a value cannot be converted.</exception>
    public static Task<List<T>> DeserializeAsync<T>(Stream utf8Stream, CsvTypeInfo<T> typeInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(utf8Stream);
        ArgumentNullException.ThrowIfNull(typeInfo);
        return ReadAllAsync(CreateParser(utf8Stream, typeInfo), typeInfo, cancellationToken);
    }

    /// <summary>Reads the records of CSV data from a <see cref="TextReader"/> asynchronously.</summary>
    /// <typeparam name="T">The type of the records.</typeparam>
    /// <param name="reader">The reader to read the data from. It is not disposed.</param>
    /// <param name="options">The options, or <see langword="null"/> to use <see cref="CsvSerializerOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The records.</returns>
    /// <exception cref="CsvException">The data is invalid or a value cannot be converted.</exception>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static Task<List<T>> DeserializeAsync<T>(TextReader reader, CsvSerializerOptions? options = null, CancellationToken cancellationToken = default) => DeserializeAsync(reader, GetTypeInfo<T>(options), cancellationToken);

    /// <summary>Reads the records of CSV data from a <see cref="TextReader"/> asynchronously.</summary>
    /// <typeparam name="T">The type of the records.</typeparam>
    /// <param name="reader">The reader to read the data from. It is not disposed.</param>
    /// <param name="typeInfo">The metadata of <typeparamref name="T"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The records.</returns>
    /// <exception cref="CsvException">The data is invalid or a value cannot be converted.</exception>
    public static Task<List<T>> DeserializeAsync<T>(TextReader reader, CsvTypeInfo<T> typeInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(typeInfo);
        return ReadAllAsync(CreateParser(reader, typeInfo), typeInfo, cancellationToken);
    }

    /// <summary>Reads the records of CSV data from a <see cref="TextReader"/> one by one, as they are enumerated.</summary>
    /// <typeparam name="T">The type of the records.</typeparam>
    /// <param name="reader">The reader to read the data from. It is not disposed.</param>
    /// <param name="options">The options, or <see langword="null"/> to use <see cref="CsvSerializerOptions.Default"/>.</param>
    /// <returns>The records. The data is read when the sequence is enumerated, and it can be enumerated once.</returns>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static IEnumerable<T> DeserializeEnumerable<T>(TextReader reader, CsvSerializerOptions? options = null) => DeserializeEnumerable(reader, GetTypeInfo<T>(options));

    /// <summary>Reads the records of CSV data from a <see cref="TextReader"/> one by one, as they are enumerated.</summary>
    /// <typeparam name="T">The type of the records.</typeparam>
    /// <param name="reader">The reader to read the data from. It is not disposed.</param>
    /// <param name="typeInfo">The metadata of <typeparamref name="T"/>.</param>
    /// <returns>The records. The data is read when the sequence is enumerated, and it can be enumerated once.</returns>
    public static IEnumerable<T> DeserializeEnumerable<T>(TextReader reader, CsvTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(typeInfo);
        return Enumerate(() => CreateParser(reader, typeInfo), typeInfo);
    }

    /// <summary>Reads the records of UTF-8 CSV data from a <see cref="Stream"/> one by one, as they are enumerated.</summary>
    /// <typeparam name="T">The type of the records.</typeparam>
    /// <param name="utf8Stream">The stream to read the UTF-8 data from. It is not disposed.</param>
    /// <param name="options">The options, or <see langword="null"/> to use <see cref="CsvSerializerOptions.Default"/>.</param>
    /// <returns>The records. The data is read when the sequence is enumerated, and it can be enumerated once.</returns>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static IEnumerable<T> DeserializeEnumerable<T>(Stream utf8Stream, CsvSerializerOptions? options = null) => DeserializeEnumerable(utf8Stream, GetTypeInfo<T>(options));

    /// <summary>Reads the records of UTF-8 CSV data from a <see cref="Stream"/> one by one, as they are enumerated.</summary>
    /// <typeparam name="T">The type of the records.</typeparam>
    /// <param name="utf8Stream">The stream to read the UTF-8 data from. It is not disposed.</param>
    /// <param name="typeInfo">The metadata of <typeparamref name="T"/>.</param>
    /// <returns>The records. The data is read when the sequence is enumerated, and it can be enumerated once.</returns>
    public static IEnumerable<T> DeserializeEnumerable<T>(Stream utf8Stream, CsvTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(utf8Stream);
        ArgumentNullException.ThrowIfNull(typeInfo);
        return Enumerate(() => CreateParser(utf8Stream, typeInfo), typeInfo);
    }

    /// <summary>Reads the records of UTF-8 CSV data from a <see cref="Stream"/> one by one, asynchronously, as they are enumerated.</summary>
    /// <typeparam name="T">The type of the records.</typeparam>
    /// <param name="utf8Stream">The stream to read the UTF-8 data from. It is not disposed.</param>
    /// <param name="options">The options, or <see langword="null"/> to use <see cref="CsvSerializerOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The records. The data is read when the sequence is enumerated, and it can be enumerated once.</returns>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static IAsyncEnumerable<T> DeserializeAsyncEnumerable<T>(Stream utf8Stream, CsvSerializerOptions? options = null, CancellationToken cancellationToken = default) => DeserializeAsyncEnumerable(utf8Stream, GetTypeInfo<T>(options), cancellationToken);

    /// <summary>Reads the records of UTF-8 CSV data from a <see cref="Stream"/> one by one, asynchronously, as they are enumerated.</summary>
    /// <typeparam name="T">The type of the records.</typeparam>
    /// <param name="utf8Stream">The stream to read the UTF-8 data from. It is not disposed.</param>
    /// <param name="typeInfo">The metadata of <typeparamref name="T"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The records. The data is read when the sequence is enumerated, and it can be enumerated once.</returns>
    public static IAsyncEnumerable<T> DeserializeAsyncEnumerable<T>(Stream utf8Stream, CsvTypeInfo<T> typeInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(utf8Stream);
        ArgumentNullException.ThrowIfNull(typeInfo);
        return EnumerateAsync(() => CreateParser(utf8Stream, typeInfo), typeInfo, cancellationToken);
    }

    /// <summary>Reads the records of CSV data from a <see cref="TextReader"/> one by one, asynchronously, as they are enumerated.</summary>
    /// <typeparam name="T">The type of the records.</typeparam>
    /// <param name="reader">The reader to read the data from. It is not disposed.</param>
    /// <param name="options">The options, or <see langword="null"/> to use <see cref="CsvSerializerOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The records. The data is read when the sequence is enumerated, and it can be enumerated once.</returns>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static IAsyncEnumerable<T> DeserializeAsyncEnumerable<T>(TextReader reader, CsvSerializerOptions? options = null, CancellationToken cancellationToken = default) => DeserializeAsyncEnumerable(reader, GetTypeInfo<T>(options), cancellationToken);

    /// <summary>Reads the records of CSV data from a <see cref="TextReader"/> one by one, asynchronously, as they are enumerated.</summary>
    /// <typeparam name="T">The type of the records.</typeparam>
    /// <param name="reader">The reader to read the data from. It is not disposed.</param>
    /// <param name="typeInfo">The metadata of <typeparamref name="T"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The records. The data is read when the sequence is enumerated, and it can be enumerated once.</returns>
    public static IAsyncEnumerable<T> DeserializeAsyncEnumerable<T>(TextReader reader, CsvTypeInfo<T> typeInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(typeInfo);
        return EnumerateAsync(() => CreateParser(reader, typeInfo), typeInfo, cancellationToken);
    }

    /// <summary>Writes records as CSV text.</summary>
    /// <typeparam name="T">The type of the records.</typeparam>
    /// <param name="values">The records.</param>
    /// <param name="options">The options, or <see langword="null"/> to use <see cref="CsvSerializerOptions.Default"/>.</param>
    /// <returns>The CSV text, with a header row when <see cref="CsvSerializerOptions.HasHeaderRow"/> is <see langword="true"/>.</returns>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static string Serialize<T>(IEnumerable<T> values, CsvSerializerOptions? options = null) => Serialize(values, GetTypeInfo<T>(options));

    /// <summary>Writes records as CSV text.</summary>
    /// <typeparam name="T">The type of the records.</typeparam>
    /// <param name="values">The records.</param>
    /// <param name="typeInfo">The metadata of <typeparamref name="T"/>.</param>
    /// <returns>The CSV text, with a header row when <see cref="CsvSerializerOptions.HasHeaderRow"/> is <see langword="true"/>.</returns>
    public static string Serialize<T>(IEnumerable<T> values, CsvTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(typeInfo);
        // Without a target, the writer keeps the text in its buffers: the string is created from them, without copying the text to another buffer first
        using var writer = new CsvWriterCore<char>(typeInfo.Options.WriterOptions, target: null);
        WriteAll(writer, values, typeInfo);
        return writer.CreateString();
    }

    /// <summary>Writes records as UTF-8 CSV data to a <see cref="Stream"/>.</summary>
    /// <typeparam name="T">The type of the records.</typeparam>
    /// <param name="utf8Stream">The stream to write the UTF-8 data to. It is flushed, not disposed.</param>
    /// <param name="values">The records.</param>
    /// <param name="options">The options, or <see langword="null"/> to use <see cref="CsvSerializerOptions.Default"/>.</param>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static void Serialize<T>(Stream utf8Stream, IEnumerable<T> values, CsvSerializerOptions? options = null) => Serialize(utf8Stream, values, GetTypeInfo<T>(options));

    /// <summary>Writes records as UTF-8 CSV data to a <see cref="Stream"/>.</summary>
    /// <typeparam name="T">The type of the records.</typeparam>
    /// <param name="utf8Stream">The stream to write the UTF-8 data to. It is flushed, not disposed.</param>
    /// <param name="values">The records.</param>
    /// <param name="typeInfo">The metadata of <typeparamref name="T"/>.</param>
    public static void Serialize<T>(Stream utf8Stream, IEnumerable<T> values, CsvTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(utf8Stream);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(typeInfo);
        using var writer = new CsvWriterCore<byte>(typeInfo.Options.WriterOptions, new StreamCsvTarget(utf8Stream, leaveOpen: true));
        WriteAll(writer, values, typeInfo);
    }

    /// <summary>Writes records as CSV text to a <see cref="TextWriter"/>.</summary>
    /// <typeparam name="T">The type of the records.</typeparam>
    /// <param name="writer">The writer to write the data to. It is flushed, not disposed.</param>
    /// <param name="values">The records.</param>
    /// <param name="options">The options, or <see langword="null"/> to use <see cref="CsvSerializerOptions.Default"/>.</param>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static void Serialize<T>(TextWriter writer, IEnumerable<T> values, CsvSerializerOptions? options = null) => Serialize(writer, values, GetTypeInfo<T>(options));

    /// <summary>Writes records as CSV text to a <see cref="TextWriter"/>.</summary>
    /// <typeparam name="T">The type of the records.</typeparam>
    /// <param name="writer">The writer to write the data to. It is flushed, not disposed.</param>
    /// <param name="values">The records.</param>
    /// <param name="typeInfo">The metadata of <typeparamref name="T"/>.</param>
    public static void Serialize<T>(TextWriter writer, IEnumerable<T> values, CsvTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(typeInfo);
        using var csvWriter = new CsvWriterCore<char>(typeInfo.Options.WriterOptions, new TextWriterCsvTarget(writer, leaveOpen: true));
        WriteAll(csvWriter, values, typeInfo);
    }

    /// <summary>Writes records as UTF-8 CSV data to an <see cref="IBufferWriter{T}"/>.</summary>
    /// <typeparam name="T">The type of the records.</typeparam>
    /// <param name="utf8BufferWriter">The buffer writer to write the UTF-8 data to.</param>
    /// <param name="values">The records.</param>
    /// <param name="options">The options, or <see langword="null"/> to use <see cref="CsvSerializerOptions.Default"/>.</param>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static void Serialize<T>(IBufferWriter<byte> utf8BufferWriter, IEnumerable<T> values, CsvSerializerOptions? options = null) => Serialize(utf8BufferWriter, values, GetTypeInfo<T>(options));

    /// <summary>Writes records as UTF-8 CSV data to an <see cref="IBufferWriter{T}"/>.</summary>
    /// <typeparam name="T">The type of the records.</typeparam>
    /// <param name="utf8BufferWriter">The buffer writer to write the UTF-8 data to.</param>
    /// <param name="values">The records.</param>
    /// <param name="typeInfo">The metadata of <typeparamref name="T"/>.</param>
    public static void Serialize<T>(IBufferWriter<byte> utf8BufferWriter, IEnumerable<T> values, CsvTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(utf8BufferWriter);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(typeInfo);
        using var writer = new CsvWriterCore<byte>(typeInfo.Options.WriterOptions, new BufferWriterCsvTarget<byte>(utf8BufferWriter));
        WriteAll(writer, values, typeInfo);
    }

    /// <summary>Writes records as UTF-8 CSV data to a <see cref="Stream"/> asynchronously.</summary>
    /// <typeparam name="T">The type of the records.</typeparam>
    /// <param name="utf8Stream">The stream to write the UTF-8 data to. It is flushed, not disposed.</param>
    /// <param name="values">The records.</param>
    /// <param name="options">The options, or <see langword="null"/> to use <see cref="CsvSerializerOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static Task SerializeAsync<T>(Stream utf8Stream, IEnumerable<T> values, CsvSerializerOptions? options = null, CancellationToken cancellationToken = default) => SerializeAsync(utf8Stream, values, GetTypeInfo<T>(options), cancellationToken);

    /// <summary>Writes records as UTF-8 CSV data to a <see cref="Stream"/> asynchronously.</summary>
    /// <typeparam name="T">The type of the records.</typeparam>
    /// <param name="utf8Stream">The stream to write the UTF-8 data to. It is flushed, not disposed.</param>
    /// <param name="values">The records.</param>
    /// <param name="typeInfo">The metadata of <typeparamref name="T"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public static Task SerializeAsync<T>(Stream utf8Stream, IEnumerable<T> values, CsvTypeInfo<T> typeInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(utf8Stream);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(typeInfo);
        return WriteAllAsync(new CsvWriterCore<byte>(typeInfo.Options.WriterOptions, new StreamCsvTarget(utf8Stream, leaveOpen: true)), values, typeInfo, cancellationToken);
    }

    /// <summary>Writes records as UTF-8 CSV data to a <see cref="Stream"/> asynchronously, as they are produced.</summary>
    /// <typeparam name="T">The type of the records.</typeparam>
    /// <param name="utf8Stream">The stream to write the UTF-8 data to. It is flushed, not disposed.</param>
    /// <param name="values">The records.</param>
    /// <param name="options">The options, or <see langword="null"/> to use <see cref="CsvSerializerOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static Task SerializeAsync<T>(Stream utf8Stream, IAsyncEnumerable<T> values, CsvSerializerOptions? options = null, CancellationToken cancellationToken = default) => SerializeAsync(utf8Stream, values, GetTypeInfo<T>(options), cancellationToken);

    /// <summary>Writes records as UTF-8 CSV data to a <see cref="Stream"/> asynchronously, as they are produced.</summary>
    /// <typeparam name="T">The type of the records.</typeparam>
    /// <param name="utf8Stream">The stream to write the UTF-8 data to. It is flushed, not disposed.</param>
    /// <param name="values">The records.</param>
    /// <param name="typeInfo">The metadata of <typeparamref name="T"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public static Task SerializeAsync<T>(Stream utf8Stream, IAsyncEnumerable<T> values, CsvTypeInfo<T> typeInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(utf8Stream);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(typeInfo);
        return WriteAllAsync(new CsvWriterCore<byte>(typeInfo.Options.WriterOptions, new StreamCsvTarget(utf8Stream, leaveOpen: true)), values, typeInfo, cancellationToken);
    }

    /// <summary>Writes records as CSV text to a <see cref="TextWriter"/> asynchronously.</summary>
    /// <typeparam name="T">The type of the records.</typeparam>
    /// <param name="writer">The writer to write the data to. It is flushed, not disposed.</param>
    /// <param name="values">The records.</param>
    /// <param name="options">The options, or <see langword="null"/> to use <see cref="CsvSerializerOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static Task SerializeAsync<T>(TextWriter writer, IEnumerable<T> values, CsvSerializerOptions? options = null, CancellationToken cancellationToken = default) => SerializeAsync(writer, values, GetTypeInfo<T>(options), cancellationToken);

    /// <summary>Writes records as CSV text to a <see cref="TextWriter"/> asynchronously.</summary>
    /// <typeparam name="T">The type of the records.</typeparam>
    /// <param name="writer">The writer to write the data to. It is flushed, not disposed.</param>
    /// <param name="values">The records.</param>
    /// <param name="typeInfo">The metadata of <typeparamref name="T"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public static Task SerializeAsync<T>(TextWriter writer, IEnumerable<T> values, CsvTypeInfo<T> typeInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(typeInfo);
        return WriteAllAsync(new CsvWriterCore<char>(typeInfo.Options.WriterOptions, new TextWriterCsvTarget(writer, leaveOpen: true)), values, typeInfo, cancellationToken);
    }

    /// <summary>Writes records as CSV text to a <see cref="TextWriter"/> asynchronously, as they are produced.</summary>
    /// <typeparam name="T">The type of the records.</typeparam>
    /// <param name="writer">The writer to write the data to. It is flushed, not disposed.</param>
    /// <param name="values">The records.</param>
    /// <param name="options">The options, or <see langword="null"/> to use <see cref="CsvSerializerOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static Task SerializeAsync<T>(TextWriter writer, IAsyncEnumerable<T> values, CsvSerializerOptions? options = null, CancellationToken cancellationToken = default) => SerializeAsync(writer, values, GetTypeInfo<T>(options), cancellationToken);

    /// <summary>Writes records as CSV text to a <see cref="TextWriter"/> asynchronously, as they are produced.</summary>
    /// <typeparam name="T">The type of the records.</typeparam>
    /// <param name="writer">The writer to write the data to. It is flushed, not disposed.</param>
    /// <param name="values">The records.</param>
    /// <param name="typeInfo">The metadata of <typeparamref name="T"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public static Task SerializeAsync<T>(TextWriter writer, IAsyncEnumerable<T> values, CsvTypeInfo<T> typeInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(typeInfo);
        return WriteAllAsync(new CsvWriterCore<char>(typeInfo.Options.WriterOptions, new TextWriterCsvTarget(writer, leaveOpen: true)), values, typeInfo, cancellationToken);
    }

    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    private static CsvTypeInfo<T> GetTypeInfo<T>(CsvSerializerOptions? options)
    {
        return (CsvTypeInfo<T>)(options ?? CsvSerializerOptions.Default).GetTypeInfoCore(typeof(T), allowReflection: true);
    }

    private static CsvParser<char> CreateParser<T>(TextReader reader, CsvTypeInfo<T> typeInfo)
        => new(typeInfo.Options.ReaderOptions, new TextReaderCsvSource(reader, leaveOpen: true), default);

    private static CsvParser<byte> CreateParser<T>(Stream stream, CsvTypeInfo<T> typeInfo)
        => new(typeInfo.Options.ReaderOptions, new StreamCsvSource(stream, leaveOpen: true), default);

    // The number of threads that convert the records, or 1 to convert them on the calling thread
    private static int GetDegreeOfParallelism<T>(CsvTypeInfo<T> typeInfo)
    {
        // The browser has a single thread: waiting for a task there would never end
        if (OperatingSystem.IsBrowser() || OperatingSystem.IsWasi())
            return 1;

        var value = typeInfo.Options.MaxDegreeOfParallelism;
        return value is -1 ? Environment.ProcessorCount : value;
    }

    private static List<T> ReadAll<T, TChar>(CsvParser<TChar> parser, CsvTypeInfo<T> typeInfo)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        // The records are not added to a list as they are read: see SegmentedListBuilder
        var result = new SegmentedListBuilder<T>();
        var degreeOfParallelism = GetDegreeOfParallelism(typeInfo);
        if (degreeOfParallelism > 1)
        {
            using var deserializer = new CsvParallelDeserializer<T, TChar>(typeInfo, degreeOfParallelism);
            while (deserializer.MoveNext(parser))
            {
                foreach (var item in deserializer.Current)
                {
                    result.Add(item);
                }
            }

            return result.ToList();
        }

        CsvRecordBinder<T>? binder = null;
        while (parser.Read())
        {
            binder ??= CsvRecordBinder<T>.Create(typeInfo, parser);
            if (binder.TryRead(parser, out var item))
            {
                result.Add(item);
            }
        }

        return result.ToList();
    }

    private static async Task<List<T>> ReadAllAsync<T, TChar>(CsvParser<TChar> parser, CsvTypeInfo<T> typeInfo, CancellationToken cancellationToken)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        using (parser)
        {
            var result = new SegmentedListBuilder<T>();
            var degreeOfParallelism = GetDegreeOfParallelism(typeInfo);
            if (degreeOfParallelism > 1)
            {
                var deserializer = new CsvParallelDeserializer<T, TChar>(typeInfo, degreeOfParallelism);
                await using (deserializer.ConfigureAwait(false))
                {
                    while (await deserializer.MoveNextAsync(parser, cancellationToken).ConfigureAwait(false))
                    {
                        foreach (var item in deserializer.Current)
                        {
                            result.Add(item);
                        }
                    }
                }

                return result.ToList();
            }

            CsvRecordBinder<T>? binder = null;
            while (await parser.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                binder ??= CsvRecordBinder<T>.Create(typeInfo, parser);
                if (binder.TryRead(parser, out var item))
                {
                    result.Add(item);
                }
            }

            return result.ToList();
        }
    }

    private static IEnumerable<T> Enumerate<T, TChar>(Func<CsvParser<TChar>> createParser, CsvTypeInfo<T> typeInfo)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        using var parser = createParser();
        var degreeOfParallelism = GetDegreeOfParallelism(typeInfo);
        if (degreeOfParallelism > 1)
        {
            using var deserializer = new CsvParallelDeserializer<T, TChar>(typeInfo, degreeOfParallelism);
            while (deserializer.MoveNext(parser))
            {
                foreach (var item in deserializer.Current)
                {
                    yield return item;
                }
            }

            yield break;
        }

        CsvRecordBinder<T>? binder = null;
        while (parser.Read())
        {
            binder ??= CsvRecordBinder<T>.Create(typeInfo, parser);
            if (binder.TryRead(parser, out var item))
            {
                yield return item;
            }
        }
    }

    private static async IAsyncEnumerable<T> EnumerateAsync<T, TChar>(Func<CsvParser<TChar>> createParser, CsvTypeInfo<T> typeInfo, [EnumeratorCancellation] CancellationToken cancellationToken)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        using var parser = createParser();
        var degreeOfParallelism = GetDegreeOfParallelism(typeInfo);
        if (degreeOfParallelism > 1)
        {
            var deserializer = new CsvParallelDeserializer<T, TChar>(typeInfo, degreeOfParallelism);
            await using (deserializer.ConfigureAwait(false))
            {
                while (await deserializer.MoveNextAsync(parser, cancellationToken).ConfigureAwait(false))
                {
                    foreach (var item in deserializer.Current)
                    {
                        yield return item;
                    }
                }
            }

            yield break;
        }

        CsvRecordBinder<T>? binder = null;
        while (await parser.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            binder ??= CsvRecordBinder<T>.Create(typeInfo, parser);
            if (binder.TryRead(parser, out var item))
            {
                yield return item;
            }
        }
    }

    private static void WriteAll<T, TChar>(CsvWriterCore<TChar> writer, IEnumerable<T> values, CsvTypeInfo<T> typeInfo)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        CsvRecordWriter.WriteHeader(writer, typeInfo);
        foreach (var value in values)
        {
            CsvRecordWriter.WriteRecord(writer, typeInfo, value);
            if (writer.ShouldFlush)
            {
                writer.FlushBuffer();
            }
        }

        writer.Flush();
    }

    private static async Task WriteAllAsync<T, TChar>(CsvWriterCore<TChar> writer, IEnumerable<T> values, CsvTypeInfo<T> typeInfo, CancellationToken cancellationToken)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        await using (writer.ConfigureAwait(false))
        {
            CsvRecordWriter.WriteHeader(writer, typeInfo);
            foreach (var value in values)
            {
                CsvRecordWriter.WriteRecord(writer, typeInfo, value);
                if (writer.ShouldFlush)
                {
                    await writer.FlushBufferAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task WriteAllAsync<T, TChar>(CsvWriterCore<TChar> writer, IAsyncEnumerable<T> values, CsvTypeInfo<T> typeInfo, CancellationToken cancellationToken)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        await using (writer.ConfigureAwait(false))
        {
            CsvRecordWriter.WriteHeader(writer, typeInfo);
            await foreach (var value in values.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                CsvRecordWriter.WriteRecord(writer, typeInfo, value);
                if (writer.ShouldFlush)
                {
                    await writer.FlushBufferAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
