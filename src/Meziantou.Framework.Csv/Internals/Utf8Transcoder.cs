using System.Buffers;
using System.Text.Unicode;

namespace Meziantou.Framework.Csv.Internals;

/// <summary>Decodes a UTF-8 value into a stack buffer, or into a pooled buffer when it is too long.</summary>
internal static class Utf8Transcoder
{
    public const int StackBufferSize = 128;

    /// <summary>Decodes a UTF-8 value into a string, as <see cref="Encoding.GetString(ReadOnlySpan{byte})"/> of <see cref="Encoding.UTF8"/> does.</summary>
    /// <remarks>
    /// Most values are ASCII: their length is the length of the string, so they are widened into the string in one pass after a validation, instead of
    /// counting the characters and decoding them. This is about a third faster for short values.
    /// </remarks>
    public static string GetString(ReadOnlySpan<byte> utf8)
    {
        if (Ascii.IsValid(utf8))
            return string.Create(utf8.Length, utf8, (destination, value) => Ascii.ToUtf16(value, destination, out _));

        return Encoding.UTF8.GetString(utf8);
    }

    public static ReadOnlySpan<char> Decode(ReadOnlySpan<byte> utf8, Span<char> stackBuffer, out char[]? rented)
    {
        // A UTF-8 sequence never decodes to more UTF-16 code units than it has bytes
        rented = null;
        var destination = utf8.Length <= stackBuffer.Length ? stackBuffer : (rented = ArrayPool<char>.Shared.Rent(utf8.Length));
        Utf8.ToUtf16(utf8, destination, out _, out var charsWritten, replaceInvalidSequences: true);
        return destination[..charsWritten];
    }

    public static void Return(char[]? rented)
    {
        if (rented is not null)
        {
            ArrayPool<char>.Shared.Return(rented);
        }
    }
}
