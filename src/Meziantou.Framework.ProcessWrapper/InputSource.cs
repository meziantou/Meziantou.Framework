using System.Text;

namespace Meziantou.Framework;

public abstract class InputSource
{
    private protected InputSource()
    {
    }

    public static InputSource FromStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return new StreamInputSource(stream);
    }

    public static InputSource FromText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new TextInputSource(text);
    }

    public static InputSource FromBytes(ReadOnlyMemory<byte> bytes)
    {
        return new BytesInputSource(bytes.ToArray());
    }

    public static InputSource FromTextReader(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new TextReaderInputSource(reader);
    }

    public static InputSource FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new FileInputSource(path);
    }

    public static InputSource FromProcessPipe(ProcessPipe processPipe)
    {
        ArgumentNullException.ThrowIfNull(processPipe);
        return new ProcessPipeInputSource(processPipe);
    }

    public static implicit operator InputSource(Stream stream) => FromStream(stream);

    public static implicit operator InputSource(string text) => FromText(text);

    public static implicit operator InputSource(byte[] bytes) => FromBytes(bytes);

    public static implicit operator InputSource(ReadOnlyMemory<byte> bytes) => FromBytes(bytes);

    public static implicit operator InputSource(TextReader reader) => FromTextReader(reader);

    public abstract int Read(byte[] buffer);

    internal virtual void NotifyProcessCompleted()
    {
    }

    private sealed class StreamInputSource(Stream stream) : InputSource
    {
        public override int Read(byte[] buffer)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            return stream.Read(buffer, 0, buffer.Length);
        }
    }

    private sealed class TextInputSource(string text) : InputSource
    {
        private readonly BytesInputSource _inner = new(Encoding.UTF8.GetBytes(text));

        public override int Read(byte[] buffer)
        {
            return _inner.Read(buffer);
        }
    }

    private sealed class BytesInputSource(byte[] bytes) : InputSource
    {
        private int _position;

        public override int Read(byte[] buffer)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            if (_position >= bytes.Length || buffer.Length == 0)
                return 0;

            var count = Math.Min(buffer.Length, bytes.Length - _position);
            bytes.AsSpan(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }
    }

    private sealed class TextReaderInputSource(TextReader reader) : InputSource
    {
        private readonly BytesInputSource _inner = new(Encoding.UTF8.GetBytes(reader.ReadToEnd()));

        public override int Read(byte[] buffer)
        {
            return _inner.Read(buffer);
        }
    }

    private sealed class FileInputSource(string path) : InputSource
    {
        private FileStream? _stream;

        public override int Read(byte[] buffer)
        {
            ArgumentNullException.ThrowIfNull(buffer);

            _stream ??= File.OpenRead(path);
            var bytesRead = _stream.Read(buffer, 0, buffer.Length);
            if (bytesRead == 0)
            {
                _stream.Dispose();
                _stream = null;
            }

            return bytesRead;
        }

        internal override void NotifyProcessCompleted()
        {
            _stream?.Dispose();
            _stream = null;
        }
    }

    private sealed class ProcessPipeInputSource(ProcessPipe processPipe) : InputSource
    {
        public override int Read(byte[] buffer)
        {
            return processPipe.Read(buffer);
        }

        internal override void NotifyProcessCompleted()
        {
            processPipe.DisposeReader();
        }
    }
}
