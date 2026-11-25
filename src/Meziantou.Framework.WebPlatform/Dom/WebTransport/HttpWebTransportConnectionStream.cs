using Meziantou.Framework.MsQuic;
using Meziantou.Framework.WebTransport;
using WebTransportErrorKind = Meziantou.Framework.WebTransport.WebTransportError;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>A stream of a <see cref="HttpWebTransportConnection"/>.</summary>
internal sealed class HttpWebTransportConnectionStream(WebTransportStream stream) : WebTransportConnectionStream
{
    private readonly Lazy<Task> _writesClosed = new(async () =>
    {
        try
        {
            await stream.WritesClosed.ConfigureAwait(false);
        }
        catch (WebTransportException ex) when (ex.Error is WebTransportErrorKind.StreamAborted)
        {
            throw new WebTransportStreamAbortedException(ex.StreamErrorCode, ex);
        }
    });

    public override bool IsBidirectional => stream.Type is MsQuicStreamType.Bidirectional;

    public override bool CanRead => stream.CanRead;

    public override bool CanWrite => stream.CanWrite;

    public override Task WritesClosed => _writesClosed.Value;

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        try
        {
            return await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (WebTransportException ex) when (ex.Error is WebTransportErrorKind.StreamAborted)
        {
            throw new WebTransportStreamAbortedException(ex.StreamErrorCode, ex);
        }
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, bool endOfStream, CancellationToken cancellationToken)
    {
        try
        {
            await stream.WriteAsync(buffer, endOfStream, cancellationToken).ConfigureAwait(false);
        }
        catch (WebTransportException ex) when (ex.Error is WebTransportErrorKind.StreamAborted)
        {
            throw new WebTransportStreamAbortedException(ex.StreamErrorCode, ex);
        }
    }

    public override void AbortRead(uint errorCode) => stream.Abort(MsQuicAbortDirection.Read, errorCode);

    public override void AbortWrite(uint errorCode) => stream.Abort(MsQuicAbortDirection.Write, errorCode);

    // The send order (a 64-bit integer, 0 by default) is mapped to the priority of MsQuic (0 to 65535, 32767 by default).
    // It is clamped before the offset is added, which would overflow for the largest values. The priority of a stream that
    // is closed does not matter: the error of MsQuic is ignored.
    public override void SetSendOrder(long sendOrder)
    {
        try
        {
            stream.Priority = (int)Math.Clamp(sendOrder, -0x7FFF, 0x8000) + 0x7FFF;
        }
        catch (Exception ex) when (ex is ObjectDisposedException or MsQuicException)
        {
        }
    }

    public override ValueTask DisposeAsync() => stream.DisposeAsync();
}
