using System.Buffers;
using Meziantou.Framework.Csv.Internals;

namespace Meziantou.Framework.Csv;

/// <summary>Writes UTF-8 encoded CSV data.</summary>
/// <remarks>
/// <para>
/// Write the values of a row with the <c>WriteField</c> methods, then end the row with <see cref="EndRow"/> or <see cref="EndRowAsync"/>. Every row
/// ends with <see cref="CsvWriterOptions.NewLine"/>, including the last one.
/// </para>
/// <para>
/// The writer keeps the data in a buffer. <see cref="EndRow"/> sends the buffer to the underlying stream when it is mostly full; the <c>WriteField</c>
/// methods never do. <see cref="EndRowAsync"/> sends it asynchronously, so a writer used only with the asynchronous methods never blocks.
/// Call <see cref="Flush"/> or <see cref="FlushAsync"/>, or dispose the writer, to send the rest of the data.
/// </para>
/// <para>The separator and the quote characters must be ASCII characters. The writer does not write a byte order mark.</para>
/// <para>This type is not thread-safe.</para>
/// </remarks>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Csv.Tests/CsvWriterTests.cs" ref:region="Utf8CsvWriter_Write" ref:dedent="true">
/// using var stream = new MemoryStream();
/// using (var writer = Utf8CsvWriter.Create(stream, leaveOpen: true))
/// {
///     writer.WriteRow("Product", "Price");
///     writer.WriteField("Café"u8); // UTF-8 bytes are written as they are
///     writer.WriteField(3.5m, "F2");
///     writer.EndRow();
/// }
///
/// Assert.Equal("Product,Price\r\nCafé,3.50\r\n", Encoding.UTF8.GetString(stream.ToArray()));
/// </code>
/// </example>
public sealed class Utf8CsvWriter : IDisposable, IAsyncDisposable
{
    private readonly CsvWriterCore<byte> _core;

    private Utf8CsvWriter(CsvWriterCore<byte> core)
    {
        _core = core;
    }

    internal CsvWriterCore<byte> Core => _core;

    /// <summary>Creates a writer that writes UTF-8 CSV data to a <see cref="Stream"/>.</summary>
    /// <param name="stream">The stream to write the data to.</param>
    /// <param name="options">The options, or <see langword="null"/> to use <see cref="CsvWriterOptions.Default"/>.</param>
    /// <param name="leaveOpen"><see langword="true"/> to leave <paramref name="stream"/> open when the <see cref="Utf8CsvWriter"/> is disposed.</param>
    /// <returns>The new writer.</returns>
    public static Utf8CsvWriter Create(Stream stream, CsvWriterOptions? options = null, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return new Utf8CsvWriter(new CsvWriterCore<byte>(options ?? CsvWriterOptions.Default, new StreamCsvTarget(stream, leaveOpen)));
    }

    /// <summary>Creates a writer that writes UTF-8 CSV data to an <see cref="IBufferWriter{T}"/>.</summary>
    /// <param name="bufferWriter">The buffer writer to write the data to.</param>
    /// <param name="options">The options, or <see langword="null"/> to use <see cref="CsvWriterOptions.Default"/>.</param>
    /// <returns>The new writer.</returns>
    public static Utf8CsvWriter Create(IBufferWriter<byte> bufferWriter, CsvWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(bufferWriter);
        return new Utf8CsvWriter(new CsvWriterCore<byte>(options ?? CsvWriterOptions.Default, new BufferWriterCsvTarget<byte>(bufferWriter)));
    }

    /// <summary>Creates a writer that writes UTF-8 CSV data to a file. The file is created or overwritten.</summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="options">The options, or <see langword="null"/> to use <see cref="CsvWriterOptions.Default"/>.</param>
    /// <returns>The new writer. Dispose it to flush the data and close the file.</returns>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The writer owns the stream, and the method disposes it when it throws")]
    public static Utf8CsvWriter CreateToFile(string path, CsvWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1, FileOptions.Asynchronous);
        try
        {
            return Create(stream, options, leaveOpen: false);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>Encodes a value to UTF-8 and writes it in the current row.</summary>
    /// <param name="value">The value. <see langword="null"/> is written as an empty value.</param>
    public void WriteField(string? value) => _core.WriteUtf16Field(value.AsSpan());

    /// <summary>Encodes a value to UTF-8 and writes it in the current row. Invalid UTF-16 sequences are replaced with U+FFFD.</summary>
    /// <param name="value">The value.</param>
    public void WriteField(ReadOnlySpan<char> value) => _core.WriteUtf16Field(value);

    /// <summary>Writes a UTF-8 value in the current row.</summary>
    /// <param name="utf8Value">The UTF-8 bytes of the value. They are written as they are.</param>
    public void WriteField(ReadOnlySpan<byte> utf8Value) => _core.WriteField(utf8Value, isText: true);

    /// <summary>Formats a value as UTF-8 with <see cref="CsvWriterOptions.FormatProvider"/> and writes it in the current row.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="value">The value.</param>
    /// <param name="format">The format string, or an empty span for the default format.</param>
    /// <param name="formatProvider">The format provider, or <see langword="null"/> to use <see cref="CsvWriterOptions.FormatProvider"/>.</param>
    /// <remarks>The value is formatted directly in the buffer of the writer, without allocating a string. <see cref="CsvWriterOptions.EscapeFormulas"/> does not apply.</remarks>
    public void WriteField<T>(T value, ReadOnlySpan<char> format = default, IFormatProvider? formatProvider = null)
        where T : IUtf8SpanFormattable
    {
        _core.WriteUtf8SpanFormattable(value, format, formatProvider);
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

    /// <summary>Writes a row with the specified values, and sends the buffer to the underlying stream asynchronously when it is mostly full.</summary>
    /// <param name="values">The values. <see langword="null"/> is written as an empty value.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public ValueTask WriteRowAsync(IEnumerable<string?> values, CancellationToken cancellationToken = default)
    {
        WriteFields(values);
        return EndRowAsync(cancellationToken);
    }

    /// <summary>Ends the current row, and sends the buffer to the underlying stream when it is mostly full.</summary>
    public void EndRow()
    {
        _core.EndRow();
        if (_core.ShouldFlush)
        {
            _core.FlushBuffer();
        }
    }

    /// <summary>Ends the current row, and sends the buffer to the underlying stream asynchronously when it is mostly full.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public ValueTask EndRowAsync(CancellationToken cancellationToken = default)
    {
        _core.EndRow();
        if (_core.ShouldFlush)
            return _core.FlushBufferAsync(cancellationToken);

        return ValueTask.CompletedTask;
    }

    /// <summary>Sends the buffered data to the underlying stream and flushes it.</summary>
    public void Flush() => _core.Flush();

    /// <summary>Sends the buffered data to the underlying stream and flushes it asynchronously.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public Task FlushAsync(CancellationToken cancellationToken = default) => _core.FlushAsync(cancellationToken);

    /// <summary>Sends the buffered data to the underlying stream, and disposes it unless the writer was created with <c>leaveOpen</c>.</summary>
    /// <remarks>A row that is not ended is written without its line terminator.</remarks>
    public void Dispose() => _core.Dispose();

    /// <summary>Sends the buffered data to the underlying stream asynchronously, and disposes it unless the writer was created with <c>leaveOpen</c>.</summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>A row that is not ended is written without its line terminator.</remarks>
    public ValueTask DisposeAsync() => _core.DisposeAsync();
}
