namespace Meziantou.Framework.Pdf.Writing;

/// <summary>Where a <see cref="PdfByteWriter"/> sends the bytes of a save when its buffer is full.</summary>
internal abstract class PdfOutputSink
{
    /// <summary>Takes the bytes of a full buffer.</summary>
    /// <param name="buffer">The buffer.</param>
    /// <param name="count">The number of bytes of the buffer to write.</param>
    /// <returns>The buffer the writer writes the next bytes to: <paramref name="buffer"/>, or a new one when the sink keeps it.</returns>
    public abstract byte[] Flush(byte[] buffer, int count);

    /// <summary>Takes a block of bytes the caller may reuse once the method returns.</summary>
    public abstract void Write(ReadOnlySpan<byte> data);

    /// <summary>Takes a block of bytes.</summary>
    /// <param name="data">The bytes.</param>
    /// <param name="isImmutable"><see langword="true"/> when the bytes are not modified until the save completes, so that the sink can keep them without a copy.</param>
    public virtual void Write(ReadOnlyMemory<byte> data, bool isImmutable) => Write(data.Span);
}
