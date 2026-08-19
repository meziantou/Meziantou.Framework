using System.Buffers;
using System.Diagnostics;

namespace Meziantou.Framework.Yaml.Serialization;

internal sealed class BufferWriterTextWriter : TextWriter
{
    private IBufferWriter<char>? _destination;

    public BufferWriterTextWriter(IBufferWriter<char> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        _destination = destination;
    }

    private BufferWriterTextWriter()
    {
    }

    public override Encoding Encoding => Encoding.UTF8;

    private IBufferWriter<char> Destination
    {
        get
        {
            ObjectDisposedException.ThrowIf(_destination is null, this);
            return _destination;
        }
    }

    /// <summary>
    /// Resets the writer to write to a new destination, so the instance can be reused in pooling scenarios.
    /// </summary>
    /// <param name="destination">The new destination buffer writer.</param>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <see langword="null"/>.</exception>
    public void Reset(IBufferWriter<char> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        _destination = destination;
    }

    internal static BufferWriterTextWriter CreateEmptyInstanceForCaching() => new();

    internal void ConfigureForCacheReuse(IBufferWriter<char> destination)
    {
        Debug.Assert(_destination is null);
        Reset(destination);
    }

    internal void ResetAllStateForCacheReuse()
    {
        // Release the reference to the caller's buffer writer so the cached instance doesn't keep it alive.
        _destination = null;
    }

    public override void Write(char value)
    {
        var destination = Destination;
        var span = destination.GetSpan(1);
        span[0] = value;
        destination.Advance(1);
    }

    public override void Write(char[] buffer, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if ((uint)index > (uint)buffer.Length) throw new ArgumentOutOfRangeException(nameof(index));
        if ((uint)count > (uint)(buffer.Length - index)) throw new ArgumentOutOfRangeException(nameof(count));

        WriteSpan(buffer.AsSpan(index, count));
    }

    public override void Write(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        WriteSpan(value.AsSpan());
    }

    public override void Write(ReadOnlySpan<char> buffer)
    {
        WriteSpan(buffer);
    }

    private void WriteSpan(ReadOnlySpan<char> buffer)
    {
        if (buffer.Length == 0)
        {
            return;
        }

        var destination = Destination;
        buffer.CopyTo(destination.GetSpan(buffer.Length));
        destination.Advance(buffer.Length);
    }
}
