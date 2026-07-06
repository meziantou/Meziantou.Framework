using System.Buffers;

namespace Meziantou.Framework.Yaml.Serialization;

internal sealed class BufferWriterTextWriter : TextWriter
{
    private readonly IBufferWriter<char> _destination;

    public BufferWriterTextWriter(IBufferWriter<char> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        _destination = destination;
    }

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        var span = _destination.GetSpan(1);
        span[0] = value;
        _destination.Advance(1);
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

        buffer.CopyTo(_destination.GetSpan(buffer.Length));
        _destination.Advance(buffer.Length);
    }
}
