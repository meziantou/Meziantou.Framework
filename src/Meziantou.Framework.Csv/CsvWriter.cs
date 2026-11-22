using System.Buffers;
using Meziantou.Framework.Csv.Internals;

namespace Meziantou.Framework.Csv;

/// <summary>Writes CSV data as UTF-16 text.</summary>
/// <remarks>
/// <para>
/// Write the values of a row with the <c>WriteField</c> methods, then end the row with <see cref="EndRow"/> or <see cref="EndRowAsync"/>. Every row
/// ends with <see cref="CsvWriterOptions.NewLine"/>, including the last one.
/// </para>
/// <para>
/// The writer keeps the data in a buffer. <see cref="EndRow"/> sends the buffer to the underlying writer when it is mostly full; the <c>WriteField</c>
/// methods never do. <see cref="EndRowAsync"/> sends it asynchronously, so a writer used only with the asynchronous methods never blocks.
/// Call <see cref="Flush"/> or <see cref="FlushAsync"/>, or dispose the writer, to send the rest of the data.
/// </para>
/// <para>To write UTF-8 data to a <see cref="Stream"/> without encoding UTF-16 text first, use <see cref="Utf8CsvWriter"/>.</para>
/// <para>This type is not thread-safe.</para>
/// </remarks>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Csv.Tests/CsvWriterTests.cs" ref:region="CsvWriter_Write" ref:dedent="true">
/// var output = new StringBuilder();
/// using (var writer = CsvWriter.Create(output, new CsvWriterOptions { NewLine = "\n" }))
/// {
///     writer.WriteRow("Name", "Age", "City");
///     writer.WriteRow("Alice", "30", "New York");
///
///     // Write a row value by value: numbers are formatted in the buffer, with the invariant culture
///     writer.WriteField("Bob");
///     writer.WriteField(25);
///     writer.WriteField("London, UK"); // Quoted: the value contains the separator
///     writer.EndRow();
/// } // Disposing the writer flushes the data
///
/// Assert.Equal("Name,Age,City\nAlice,30,New York\nBob,25,\"London, UK\"\n", output.ToString());
/// </code>
/// </example>
public sealed class CsvWriter : IDisposable, IAsyncDisposable
{
    private readonly CsvWriterCore<char> _core;

    private CsvWriter(CsvWriterCore<char> core)
    {
        _core = core;
    }

    internal CsvWriterCore<char> Core => _core;

    /// <summary>Creates a writer that writes CSV data to a <see cref="TextWriter"/>.</summary>
    /// <param name="writer">The writer to write the data to.</param>
    /// <param name="options">The options, or <see langword="null"/> to use <see cref="CsvWriterOptions.Default"/>.</param>
    /// <param name="leaveOpen"><see langword="true"/> to leave <paramref name="writer"/> open when the <see cref="CsvWriter"/> is disposed.</param>
    /// <returns>The new writer.</returns>
    public static CsvWriter Create(TextWriter writer, CsvWriterOptions? options = null, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(writer);
        return new CsvWriter(new CsvWriterCore<char>(options ?? CsvWriterOptions.Default, new TextWriterCsvTarget(writer, leaveOpen)));
    }

    /// <summary>Creates a writer that writes CSV data to a <see cref="Stream"/>.</summary>
    /// <param name="stream">The stream to write the data to.</param>
    /// <param name="encoding">The encoding of the data, or <see langword="null"/> for UTF-8 without a byte order mark.</param>
    /// <param name="options">The options, or <see langword="null"/> to use <see cref="CsvWriterOptions.Default"/>.</param>
    /// <param name="leaveOpen"><see langword="true"/> to leave <paramref name="stream"/> open when the <see cref="CsvWriter"/> is disposed.</param>
    /// <returns>The new writer.</returns>
    /// <remarks>To write UTF-8 data without encoding UTF-16 text first, use <see cref="Utf8CsvWriter.Create(Stream, CsvWriterOptions?, bool)"/>.</remarks>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The writer owns the StreamWriter, which leaves the stream open as the caller asked")]
    public static CsvWriter Create(Stream stream, Encoding? encoding = null, CsvWriterOptions? options = null, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var writer = new StreamWriter(stream, encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: -1, leaveOpen: leaveOpen);
        return Create(writer, options, leaveOpen: false);
    }

    /// <summary>Creates a writer that writes CSV data to an <see cref="IBufferWriter{T}"/>.</summary>
    /// <param name="bufferWriter">The buffer writer to write the data to.</param>
    /// <param name="options">The options, or <see langword="null"/> to use <see cref="CsvWriterOptions.Default"/>.</param>
    /// <returns>The new writer.</returns>
    public static CsvWriter Create(IBufferWriter<char> bufferWriter, CsvWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(bufferWriter);
        return new CsvWriter(new CsvWriterCore<char>(options ?? CsvWriterOptions.Default, new BufferWriterCsvTarget<char>(bufferWriter)));
    }

    /// <summary>Creates a writer that appends CSV data to a <see cref="StringBuilder"/>.</summary>
    /// <param name="builder">The builder to append the data to.</param>
    /// <param name="options">The options, or <see langword="null"/> to use <see cref="CsvWriterOptions.Default"/>.</param>
    /// <returns>The new writer.</returns>
    public static CsvWriter Create(StringBuilder builder, CsvWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return new CsvWriter(new CsvWriterCore<char>(options ?? CsvWriterOptions.Default, new StringBuilderCsvTarget(builder)));
    }

    /// <summary>Creates a writer that writes CSV data to a file. The file is created or overwritten.</summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="encoding">The encoding of the file, or <see langword="null"/> for UTF-8 without a byte order mark.</param>
    /// <param name="options">The options, or <see langword="null"/> to use <see cref="CsvWriterOptions.Default"/>.</param>
    /// <returns>The new writer. Dispose it to flush the data and close the file.</returns>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The writer owns the stream, and the method disposes it when it throws")]
    public static CsvWriter CreateToFile(string path, Encoding? encoding = null, CsvWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1, FileOptions.Asynchronous);
        try
        {
            return Create(stream, encoding, options, leaveOpen: false);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>Writes a value of the current row.</summary>
    /// <param name="value">The value. <see langword="null"/> is written as an empty value.</param>
    public void WriteField(string? value) => _core.WriteField(value.AsSpan(), isText: true);

    /// <summary>Writes a value of the current row.</summary>
    /// <param name="value">The value.</param>
    public void WriteField(ReadOnlySpan<char> value) => _core.WriteField(value, isText: true);

    /// <summary>Formats a value with <see cref="CsvWriterOptions.FormatProvider"/> and writes it in the current row.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="value">The value.</param>
    /// <param name="format">The format string, or an empty span for the default format.</param>
    /// <param name="formatProvider">The format provider, or <see langword="null"/> to use <see cref="CsvWriterOptions.FormatProvider"/>.</param>
    /// <remarks>The value is formatted directly in the buffer of the writer, without allocating a string. <see cref="CsvWriterOptions.EscapeFormulas"/> does not apply.</remarks>
    public void WriteField<T>(T value, ReadOnlySpan<char> format = default, IFormatProvider? formatProvider = null)
        where T : ISpanFormattable
    {
        _core.WriteSpanFormattable(value, format, formatProvider);
    }

    /// <summary>Writes values in the current row.</summary>
    /// <param name="values">The values. <see langword="null"/> is written as an empty value.</param>
    public void WriteFields(params ReadOnlySpan<string?> values)
    {
        foreach (var value in values)
        {
            WriteField(value);
        }
    }

    /// <summary>Writes values in the current row.</summary>
    /// <param name="values">The values. <see langword="null"/> is written as an empty value.</param>
    public void WriteFields(IEnumerable<string?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        foreach (var value in values)
        {
            WriteField(value);
        }
    }

    /// <summary>Writes a row with the specified values.</summary>
    /// <param name="values">The values. <see langword="null"/> is written as an empty value.</param>
    public void WriteRow(params ReadOnlySpan<string?> values)
    {
        WriteFields(values);
        EndRow();
    }

    /// <summary>Writes a row with the specified values.</summary>
    /// <param name="values">The values. <see langword="null"/> is written as an empty value.</param>
    public void WriteRow(IEnumerable<string?> values)
    {
        WriteFields(values);
        EndRow();
    }

    /// <summary>Writes a row with the specified values, and sends the buffer to the underlying writer asynchronously when it is mostly full.</summary>
    /// <param name="values">The values. <see langword="null"/> is written as an empty value.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public ValueTask WriteRowAsync(IEnumerable<string?> values, CancellationToken cancellationToken = default)
    {
        WriteFields(values);
        return EndRowAsync(cancellationToken);
    }

    /// <summary>Ends the current row, and sends the buffer to the underlying writer when it is mostly full.</summary>
    public void EndRow()
    {
        _core.EndRow();
        if (_core.ShouldFlush)
        {
            _core.FlushBuffer();
        }
    }

    /// <summary>Ends the current row, and sends the buffer to the underlying writer asynchronously when it is mostly full.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public ValueTask EndRowAsync(CancellationToken cancellationToken = default)
    {
        _core.EndRow();
        if (_core.ShouldFlush)
            return _core.FlushBufferAsync(cancellationToken);

        return ValueTask.CompletedTask;
    }

    /// <summary>Sends the buffered data to the underlying writer and flushes it.</summary>
    public void Flush() => _core.Flush();

    /// <summary>Sends the buffered data to the underlying writer and flushes it asynchronously.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public Task FlushAsync(CancellationToken cancellationToken = default) => _core.FlushAsync(cancellationToken);

    /// <summary>Sends the buffered data to the underlying writer, and disposes it unless the writer was created with <c>leaveOpen</c>.</summary>
    /// <remarks>A row that is not ended is written without its line terminator.</remarks>
    public void Dispose() => _core.Dispose();

    /// <summary>Sends the buffered data to the underlying writer asynchronously, and disposes it unless the writer was created with <c>leaveOpen</c>.</summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>A row that is not ended is written without its line terminator.</remarks>
    public ValueTask DisposeAsync() => _core.DisposeAsync();
}
