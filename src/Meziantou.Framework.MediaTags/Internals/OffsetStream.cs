namespace Meziantou.Framework.MediaTags.Internals;

/// <summary>
/// Presents the part of a seekable stream that starts at a given offset as if it were a whole file.
/// </summary>
/// <remarks>
/// Format detection reads from the caller's current position and restores it, but every parser addresses the
/// file from offset 0. Without this shim a stream positioned part-way through would be detected at one offset
/// and then parsed from another, so the tags would be read from a different file than the one identified.
/// </remarks>
internal sealed class OffsetStream(Stream inner, long origin) : Stream
{
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => inner.Length - origin;

    public override long Position
    {
        get => inner.Position - origin;
        set => inner.Position = origin + value;
    }

    public override void Flush() => inner.Flush();

    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

    public override int Read(Span<byte> buffer) => inner.Read(buffer);

    public override long Seek(long offset, SeekOrigin origin1)
    {
        var target = origin1 switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => Position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin1)),
        };

        Position = target;
        return target;
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        // The caller owns the underlying stream.
        base.Dispose(disposing);
    }
}
