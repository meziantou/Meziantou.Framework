using System.Buffers;
using System.Text.Unicode;
using Meziantou.Framework.Csv.Internals;

namespace Meziantou.Framework.Csv.Serialization;

/// <summary>Converts the values of a column of type <typeparamref name="T"/> to or from text.</summary>
/// <typeparam name="T">The type of the values.</typeparam>
/// <remarks>
/// <para>
/// Override the UTF-16 methods. The UTF-8 methods, used to read and write UTF-8 data, decode the value and call the UTF-16 methods by default:
/// override them too to avoid the conversion.
/// </para>
/// <para>
/// When <see cref="HandleNull"/> is <see langword="false"/> and <typeparamref name="T"/> can be <see langword="null"/>, an empty value is read as
/// <see langword="null"/> and a <see langword="null"/> value is written as an empty value, without calling the converter.
/// </para>
/// </remarks>
public abstract class CsvConverter<T> : CsvConverter
{
    /// <summary>Initializes a new instance of the <see cref="CsvConverter{T}"/> class.</summary>
    protected CsvConverter()
    {
    }

    /// <inheritdoc/>
    public sealed override Type TypeToConvert => typeof(T);

    /// <summary>Gets a value indicating whether the converter reads empty values and writes <see langword="null"/> values itself. Default is <see langword="false"/>.</summary>
    public virtual bool HandleNull => false;

    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(T);

    /// <summary>Converts a value read from the data.</summary>
    /// <param name="value">The value, without its quotes.</param>
    /// <param name="context">The context of the conversion.</param>
    /// <returns>The converted value.</returns>
    /// <exception cref="FormatException">The value is not valid. The serializer reports it as a <see cref="CsvException"/> with the position of the value.</exception>
    public abstract T Read(ReadOnlySpan<char> value, CsvConverterContext context);

    /// <summary>Converts a UTF-8 value read from the data.</summary>
    /// <param name="utf8Value">The UTF-8 bytes of the value, without its quotes.</param>
    /// <param name="context">The context of the conversion.</param>
    /// <returns>The converted value.</returns>
    /// <remarks>The default implementation decodes the value and calls <see cref="Read(ReadOnlySpan{char}, CsvConverterContext)"/>.</remarks>
    public virtual T Read(ReadOnlySpan<byte> utf8Value, CsvConverterContext context)
    {
        Span<char> buffer = unsafe(stackalloc char[Utf8Transcoder.StackBufferSize]);
        var chars = Utf8Transcoder.Decode(utf8Value, buffer, out var rented);
        try
        {
            return Read(chars, context);
        }
        finally
        {
            Utf8Transcoder.Return(rented);
        }
    }

    /// <summary>Formats a value to write in the data.</summary>
    /// <param name="value">The value.</param>
    /// <param name="destination">The span to write the text to. The writer quotes and escapes the text when needed.</param>
    /// <param name="charsWritten">The number of characters written to <paramref name="destination"/>.</param>
    /// <param name="context">The context of the conversion.</param>
    /// <returns><see langword="true"/> when the text fits in <paramref name="destination"/>; otherwise, <see langword="false"/>, and the writer calls the method again with a larger span.</returns>
    public abstract bool TryFormat(T value, Span<char> destination, out int charsWritten, CsvConverterContext context);

    /// <summary>Formats a value as UTF-8 to write in the data.</summary>
    /// <param name="value">The value.</param>
    /// <param name="utf8Destination">The span to write the UTF-8 bytes to. The writer quotes and escapes the text when needed.</param>
    /// <param name="bytesWritten">The number of bytes written to <paramref name="utf8Destination"/>.</param>
    /// <param name="context">The context of the conversion.</param>
    /// <returns><see langword="true"/> when the text fits in <paramref name="utf8Destination"/>; otherwise, <see langword="false"/>, and the writer calls the method again with a larger span.</returns>
    /// <remarks>The default implementation calls <see cref="TryFormat(T, Span{char}, out int, CsvConverterContext)"/> and encodes the text.</remarks>
    public virtual bool TryFormat(T value, Span<byte> utf8Destination, out int bytesWritten, CsvConverterContext context)
    {
        var size = Math.Max(utf8Destination.Length, 128);
        while (true)
        {
            var buffer = ArrayPool<char>.Shared.Rent(size);
            try
            {
                if (TryFormat(value, buffer, out var charsWritten, context))
                {
                    var status = Utf8.FromUtf16(buffer.AsSpan(0, charsWritten), utf8Destination, out _, out bytesWritten, replaceInvalidSequences: true);
                    return status is OperationStatus.Done;
                }
            }
            finally
            {
                ArrayPool<char>.Shared.Return(buffer);
            }

            size = CsvWriterCore<char>.GrowFormatSize(size);
        }
    }
}
