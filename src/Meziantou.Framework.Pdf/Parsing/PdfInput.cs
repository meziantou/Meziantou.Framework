namespace Meziantou.Framework.Pdf.Parsing;

/// <summary>
/// The bytes of a file the reader reads: in memory, or read on demand from a seekable stream. The offsets are 64-bit, so
/// that a stream can be larger than an array.
/// </summary>
internal abstract class PdfInput
{
    public abstract long Length { get; }

    /// <summary>Gets a value indicating whether the bytes are in memory: the ranges are slices of it, nothing is copied.</summary>
    public abstract bool IsInMemory { get; }

    /// <summary>Reads the bytes at an offset.</summary>
    /// <returns>The number of bytes read: fewer than the length of <paramref name="destination"/> only at the end of the input.</returns>
    public abstract int Read(long offset, Span<byte> destination);

    /// <summary>Gets the bytes of a range, which the caller may keep: a slice of the memory, or a new array.</summary>
    public virtual ReadOnlyMemory<byte> GetMemory(long offset, int length)
    {
        var buffer = new byte[length];
        var read = Read(offset, buffer);
        return buffer.AsMemory(0, read);
    }

    /// <summary>Gets the last byte of the input, or -1 when it is empty.</summary>
    public int GetLastByte()
    {
        if (Length == 0)
            return -1;

        Span<byte> buffer = stackalloc byte[1];
        return Read(Length - 1, buffer) == 1 ? buffer[0] : -1;
    }
}
