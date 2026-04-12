using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;

namespace Meziantou.Framework;

public abstract class OutputTarget
{
    private protected OutputTarget()
    {
    }

    public static OutputTarget ToStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return new StreamOutputTarget(stream);
    }

    public static OutputTarget ToStringBuilder(StringBuilder stringBuilder)
    {
        ArgumentNullException.ThrowIfNull(stringBuilder);
        return new StringBuilderOutputTarget(stringBuilder);
    }

    public static OutputTarget ToTextDelegate(Action<string> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new TextDelegateOutputTarget(handler);
    }

    public static OutputTarget ToTextWriter(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        return new TextWriterOutputTarget(writer);
    }

    public static OutputTarget ToBytesDelegate(Action<byte[]> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new BytesDelegateOutputTarget(handler);
    }

    public static OutputTarget ToProcessOutputCollection(ProcessOutputCollection collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        return new ProcessOutputCollectionOutputTarget(collection, ProcessOutputType.StandardOutput);
    }

    public static OutputTarget ToProcessPipe(ProcessPipe pipe)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        return new ProcessPipeOutputTarget(pipe);
    }

    public static implicit operator OutputTarget(Stream stream) => ToStream(stream);

    public static implicit operator OutputTarget(StringBuilder stringBuilder) => ToStringBuilder(stringBuilder);

    public static implicit operator OutputTarget(Action<string> handler) => ToTextDelegate(handler);

    public static implicit operator OutputTarget(TextWriter writer) => ToTextWriter(writer);

    public static implicit operator OutputTarget(ProcessOutputCollection collection) => ToProcessOutputCollection(collection);

    public abstract void Write(ReadOnlySpan<byte> write);

    public virtual void NotifyProcessCompleted()
    {
    }

    internal virtual void SetEncoding(Encoding encoding)
    {
    }

    internal virtual OutputTarget ForOutputType(ProcessOutputType outputType) => this;

    private sealed class StreamOutputTarget(Stream stream) : OutputTarget
    {
        private readonly Stream _stream = SynchronizedWriteStream.Create(stream);

        public override void Write(ReadOnlySpan<byte> write)
        {
            _stream.Write(write);
        }

        public override void NotifyProcessCompleted()
        {
            _stream.Flush();
        }
    }

    private sealed class StringBuilderOutputTarget(StringBuilder stringBuilder) : OutputTarget
    {
        private readonly Lock _syncObject = new();
        private Decoder _decoder = Encoding.UTF8.GetDecoder();

        public override void Write(ReadOnlySpan<byte> write)
        {
            if (write.IsEmpty)
                return;

            lock (_syncObject)
            {
                var charCount = _decoder.GetCharCount(write, flush: false);
                if (charCount == 0)
                    return;

                var chars = new char[charCount];
                var charsRead = _decoder.GetChars(write, chars, flush: false);
                stringBuilder.Append(chars, 0, charsRead);
            }
        }

        public override void NotifyProcessCompleted()
        {
            lock (_syncObject)
            {
                var charCount = _decoder.GetCharCount(ReadOnlySpan<byte>.Empty, flush: true);
                if (charCount == 0)
                    return;

                var chars = new char[charCount];
                var charsRead = _decoder.GetChars(ReadOnlySpan<byte>.Empty, chars, flush: true);
                stringBuilder.Append(chars, 0, charsRead);
            }
        }

        internal override void SetEncoding(Encoding encoding)
        {
            lock (_syncObject)
            {
                _decoder = encoding.GetDecoder();
            }
        }
    }

    private sealed class TextDelegateOutputTarget(Action<string> handler) : LineBasedTextOutputTarget
    {
        protected override void HandleLine(string line)
        {
            handler(line);
        }
    }

    private sealed class TextWriterOutputTarget(TextWriter writer) : LineBasedTextOutputTarget
    {
        protected override void HandleLine(string line)
        {
            SynchronizedTextWriter.WriteLine(writer, line);
        }
    }

    private sealed class BytesDelegateOutputTarget(Action<byte[]> handler) : OutputTarget
    {
        private readonly Lock _syncObject = new();

        public override void Write(ReadOnlySpan<byte> write)
        {
            var copy = write.ToArray();
            lock (_syncObject)
            {
                handler(copy);
            }
        }
    }

    private sealed class ProcessOutputCollectionOutputTarget(ProcessOutputCollection collection, ProcessOutputType outputType) : LineBasedTextOutputTarget
    {
        protected override void HandleLine(string line)
        {
            collection.Add(outputType, line);
        }

        internal override OutputTarget ForOutputType(ProcessOutputType targetOutputType)
        {
            return new ProcessOutputCollectionOutputTarget(collection, targetOutputType);
        }
    }

    private sealed class ProcessPipeOutputTarget(ProcessPipe pipe) : OutputTarget
    {
        public override void Write(ReadOnlySpan<byte> write)
        {
            pipe.Write(write);
        }

        public override void NotifyProcessCompleted()
        {
            pipe.DisposeWriter();
        }
    }

    private abstract class LineBasedTextOutputTarget : OutputTarget
    {
        private readonly Lock _syncObject = new();
        private Decoder _decoder = Encoding.UTF8.GetDecoder();
        private readonly StringBuilder _lineBuilder = new();
        private bool _lastCharacterWasCarriageReturn;

        public override void Write(ReadOnlySpan<byte> write)
        {
            if (write.IsEmpty)
                return;

            lock (_syncObject)
            {
                var charCount = _decoder.GetCharCount(write, flush: false);
                if (charCount == 0)
                    return;

                var chars = new char[charCount];
                var charsRead = _decoder.GetChars(write, chars, flush: false);
                DispatchLines(chars.AsSpan(0, charsRead));
            }
        }

        public override void NotifyProcessCompleted()
        {
            lock (_syncObject)
            {
                var charCount = _decoder.GetCharCount(ReadOnlySpan<byte>.Empty, flush: true);
                if (charCount > 0)
                {
                    var chars = new char[charCount];
                    var charsRead = _decoder.GetChars(ReadOnlySpan<byte>.Empty, chars, flush: true);
                    DispatchLines(chars.AsSpan(0, charsRead));
                }

                if (_lastCharacterWasCarriageReturn || _lineBuilder.Length > 0)
                {
                    DispatchLine();
                }
            }
        }

        internal override void SetEncoding(Encoding encoding)
        {
            lock (_syncObject)
            {
                _decoder = encoding.GetDecoder();
                _lineBuilder.Clear();
                _lastCharacterWasCarriageReturn = false;
            }
        }

        protected abstract void HandleLine(string line);

        private void DispatchLines(ReadOnlySpan<char> chars)
        {
            foreach (var character in chars)
            {
                if (_lastCharacterWasCarriageReturn)
                {
                    _lastCharacterWasCarriageReturn = false;
                    if (character == '\n')
                    {
                        continue;
                    }
                }

                if (character == '\r')
                {
                    DispatchLine();
                    _lastCharacterWasCarriageReturn = true;
                    continue;
                }

                if (character == '\n')
                {
                    DispatchLine();
                    continue;
                }

                _lineBuilder.Append(character);
            }
        }

        private void DispatchLine()
        {
            var line = _lineBuilder.ToString();
            _lineBuilder.Clear();
            HandleLine(line);
        }
    }

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The wrapped stream and semaphore are shared and owned by the caller/ConditionalWeakTable.")]
    private sealed class SynchronizedWriteStream : Stream
    {
        private static readonly ConditionalWeakTable<Stream, SemaphoreSlim> SemaphoreByStream = new();

        private readonly Stream _stream;
        private readonly SemaphoreSlim _semaphore;

        private SynchronizedWriteStream(Stream stream)
        {
            _stream = stream;
            _semaphore = SemaphoreByStream.GetValue(stream, static _ => new SemaphoreSlim(1, 1));
        }

        public override bool CanRead => _stream.CanRead;

        public override bool CanSeek => _stream.CanSeek;

        public override bool CanWrite => _stream.CanWrite;

        public override long Length => _stream.Length;

        public override long Position
        {
            get => _stream.Position;
            set => _stream.Position = value;
        }

        public static Stream Create(Stream stream)
        {
            if (stream is SynchronizedWriteStream)
                return stream;

            return new SynchronizedWriteStream(stream);
        }

        public override void Flush()
        {
            _semaphore.Wait();
            try
            {
                _stream.Flush();
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return FlushAsyncCore(cancellationToken);
        }

        private async Task FlushAsyncCore(CancellationToken cancellationToken)
        {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public override int Read(byte[] buffer, int offset, int count) => _stream.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => _stream.Seek(offset, origin);

        public override void SetLength(long value) => _stream.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count)
        {
            _semaphore.Wait();
            try
            {
                _stream.Write(buffer, offset, count);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _semaphore.Wait();
            try
            {
                _stream.Write(buffer);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return WriteAsyncCore(buffer, cancellationToken);
        }

        private async ValueTask WriteAsyncCore(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }

    private static class SynchronizedTextWriter
    {
        private static readonly ConditionalWeakTable<TextWriter, SemaphoreSlim> SemaphoreByWriter = new();

        public static void WriteLine(TextWriter writer, string line)
        {
            var semaphore = SemaphoreByWriter.GetValue(writer, static _ => new SemaphoreSlim(1, 1));
            semaphore.Wait();
            try
            {
                writer.WriteLine(line);
            }
            finally
            {
                semaphore.Release();
            }
        }
    }
}
