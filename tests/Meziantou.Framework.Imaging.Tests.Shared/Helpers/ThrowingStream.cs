namespace Meziantou.Framework.Imaging.Tests;

/// <summary>A readable and writable stream that fails the test if any I/O is attempted.</summary>
internal sealed class ThrowingStream : Stream
{
    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new InvalidOperationException("Unexpected I/O: Length");

    public override long Position
    {
        get => throw new InvalidOperationException("Unexpected I/O: Position");
        set => throw new InvalidOperationException("Unexpected I/O: Position");
    }

    public override void Flush() => throw new InvalidOperationException("Unexpected I/O: Flush");

    public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Unexpected I/O: Read");

    public override long Seek(long offset, SeekOrigin origin) => throw new InvalidOperationException("Unexpected I/O: Seek");

    public override void SetLength(long value) => throw new InvalidOperationException("Unexpected I/O: SetLength");

    public override void Write(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Unexpected I/O: Write");
}
