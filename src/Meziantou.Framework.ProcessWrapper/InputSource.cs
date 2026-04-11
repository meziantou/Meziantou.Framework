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

    public static implicit operator InputSource(Stream stream) => FromStream(stream);

    public static implicit operator InputSource(string text) => FromText(text);

    public static implicit operator InputSource(byte[] bytes) => FromBytes(bytes);

    public static implicit operator InputSource(ReadOnlyMemory<byte> bytes) => FromBytes(bytes);

    public static implicit operator InputSource(TextReader reader) => FromTextReader(reader);

    internal abstract Task WriteAsync(StreamWriter standardInput, CancellationToken cancellationToken);

    private sealed class StreamInputSource(Stream stream) : InputSource
    {
        internal override async Task WriteAsync(StreamWriter standardInput, CancellationToken cancellationToken)
        {
            await stream.CopyToAsync(standardInput.BaseStream, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class TextInputSource(string text) : InputSource
    {
        internal override async Task WriteAsync(StreamWriter standardInput, CancellationToken cancellationToken)
        {
            await standardInput.WriteAsync(text.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class BytesInputSource(byte[] bytes) : InputSource
    {
        internal override async Task WriteAsync(StreamWriter standardInput, CancellationToken cancellationToken)
        {
            await standardInput.BaseStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class TextReaderInputSource(TextReader reader) : InputSource
    {
        internal override async Task WriteAsync(StreamWriter standardInput, CancellationToken cancellationToken)
        {
            var buffer = new char[4096];
            int charsRead;
            while ((charsRead = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
            {
                await standardInput.WriteAsync(buffer.AsMemory(0, charsRead), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private sealed class FileInputSource(string path) : InputSource
    {
        internal override async Task WriteAsync(StreamWriter standardInput, CancellationToken cancellationToken)
        {
            using var fileStream = File.OpenRead(path);
            await fileStream.CopyToAsync(standardInput.BaseStream, cancellationToken).ConfigureAwait(false);
        }
    }
}
