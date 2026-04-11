using System.Collections.Immutable;
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
        return new ProcessOutputCollectionOutputTarget(collection);
    }

    public static implicit operator OutputTarget(Stream stream) => ToStream(stream);

    public static implicit operator OutputTarget(StringBuilder stringBuilder) => ToStringBuilder(stringBuilder);

    public static implicit operator OutputTarget(Action<string> handler) => ToTextDelegate(handler);

    public static implicit operator OutputTarget(TextWriter writer) => ToTextWriter(writer);

    public static implicit operator OutputTarget(ProcessOutputCollection collection) => ToProcessOutputCollection(collection);

    internal abstract void AddHandlers(ImmutableArray<Action<string>>.Builder outputHandlers, ImmutableArray<Stream>.Builder outputBinaryHandlers, ProcessOutputType outputType);

    private sealed class StreamOutputTarget(Stream stream) : OutputTarget
    {
        internal override void AddHandlers(ImmutableArray<Action<string>>.Builder outputHandlers, ImmutableArray<Stream>.Builder outputBinaryHandlers, ProcessOutputType outputType)
        {
            outputBinaryHandlers.Add(SynchronizedWriteStream.Create(stream));
        }
    }

    private sealed class StringBuilderOutputTarget(StringBuilder stringBuilder) : OutputTarget
    {
        internal override void AddHandlers(ImmutableArray<Action<string>>.Builder outputHandlers, ImmutableArray<Stream>.Builder outputBinaryHandlers, ProcessOutputType outputType)
        {
            outputBinaryHandlers.Add(new StringBuilderOutputStream(stringBuilder));
        }
    }

    private sealed class TextDelegateOutputTarget(Action<string> handler) : OutputTarget
    {
        internal override void AddHandlers(ImmutableArray<Action<string>>.Builder outputHandlers, ImmutableArray<Stream>.Builder outputBinaryHandlers, ProcessOutputType outputType)
        {
            outputHandlers.Add(handler);
        }
    }

    private sealed class TextWriterOutputTarget(TextWriter writer) : OutputTarget
    {
        internal override void AddHandlers(ImmutableArray<Action<string>>.Builder outputHandlers, ImmutableArray<Stream>.Builder outputBinaryHandlers, ProcessOutputType outputType)
        {
            outputHandlers.Add(line => SynchronizedTextWriter.WriteLine(writer, line));
        }
    }

    private sealed class BytesDelegateOutputTarget(Action<byte[]> handler) : OutputTarget
    {
        internal override void AddHandlers(ImmutableArray<Action<string>>.Builder outputHandlers, ImmutableArray<Stream>.Builder outputBinaryHandlers, ProcessOutputType outputType)
        {
            outputBinaryHandlers.Add(new BytesDelegateOutputStream(handler));
        }
    }

    private sealed class ProcessOutputCollectionOutputTarget(ProcessOutputCollection collection) : OutputTarget
    {
        internal override void AddHandlers(ImmutableArray<Action<string>>.Builder outputHandlers, ImmutableArray<Stream>.Builder outputBinaryHandlers, ProcessOutputType outputType)
        {
            outputHandlers.Add(line => collection.Add(outputType, line));
        }
    }

    private sealed class BytesDelegateOutputStream(Action<byte[]> handler) : Stream
    {
        private readonly object _syncObject = new();

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            WriteCore(buffer.AsSpan(offset, count));
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            WriteCore(buffer);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteCore(buffer.AsSpan(offset, count));
            return Task.CompletedTask;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteCore(buffer.Span);
            return ValueTask.CompletedTask;
        }

        private void WriteCore(ReadOnlySpan<byte> buffer)
        {
            var copy = buffer.ToArray();
            lock (_syncObject)
            {
                handler(copy);
            }
        }
    }

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
