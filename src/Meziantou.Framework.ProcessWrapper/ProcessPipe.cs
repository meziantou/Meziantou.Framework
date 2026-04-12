using System.IO.Pipelines;

namespace Meziantou.Framework;

public sealed class ProcessPipe
{
    private const long DefaultPauseWriterThreshold = 64 * 1024;

    private readonly Stream _inputStream;
    private readonly Stream _outputStream;
    private readonly Lock _syncObject = new();
    private bool _isWriterDisposed;
    private bool _isReaderDisposed;

    public ProcessPipe(long? maxBufferSize = null)
    {
        if (maxBufferSize is <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBufferSize), maxBufferSize, "Max buffer size must be greater than 0.");

        var pauseWriterThreshold = maxBufferSize ?? DefaultPauseWriterThreshold;
        var resumeWriterThreshold = Math.Max(1, pauseWriterThreshold / 2);

        var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: pauseWriterThreshold, resumeWriterThreshold: resumeWriterThreshold, useSynchronizationContext: false));
        _inputStream = pipe.Reader.AsStream();
        _outputStream = pipe.Writer.AsStream();
    }

    public static implicit operator InputSource(ProcessPipe pipe)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        return InputSource.FromProcessPipe(pipe);
    }

    public static implicit operator OutputTarget(ProcessPipe pipe)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        return OutputTarget.ToProcessPipe(pipe);
    }

    internal int Read(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ThrowIfReaderDisposed();

        var bytesRead = _inputStream.Read(buffer, 0, buffer.Length);
        if (bytesRead == 0)
        {
            DisposeReader();
        }

        return bytesRead;
    }

    internal void DisposeReader()
    {
        var shouldDispose = false;
        lock (_syncObject)
        {
            if (!_isReaderDisposed)
            {
                _isReaderDisposed = true;
                shouldDispose = true;
            }
        }

        if (shouldDispose)
        {
            _inputStream.Dispose();
        }
    }

    internal void Write(ReadOnlySpan<byte> buffer)
    {
        lock (_syncObject)
        {
            ThrowIfWriterDisposed();
            _outputStream.Write(buffer);
        }
    }

    internal void DisposeWriter()
    {
        DisposeWriterCore();
    }

    private void ThrowIfWriterDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isWriterDisposed), this);
    }

    private void ThrowIfReaderDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isReaderDisposed), this);
    }

    private void DisposeWriterCore()
    {
        var shouldDispose = false;
        lock (_syncObject)
        {
            if (!_isWriterDisposed)
            {
                _isWriterDisposed = true;
                shouldDispose = true;
            }
        }

        if (shouldDispose)
        {
            _outputStream.Dispose();
        }
    }
}
