using System.Text;

namespace Meziantou.Framework.WasmEngine.Text;

/// <summary>Writes the binary format.</summary>
internal sealed class ByteWriter
{
    private readonly List<byte> _bytes = [];

    public int Length => _bytes.Count;

    public void Byte(byte value) => _bytes.Add(value);

    public void Bytes(ReadOnlySpan<byte> value)
    {
        foreach (var b in value)
        {
            _bytes.Add(b);
        }
    }

    public void Append(ByteWriter other) => _bytes.AddRange(other._bytes);

    public void U32(ulong value)
    {
        do
        {
            var b = (byte)(value & 0x7F);
            value >>= 7;
            _bytes.Add(value == 0 ? b : (byte)(b | 0x80));
        }
        while (value != 0);
    }

    public void S64(long value)
    {
        while (true)
        {
            var b = (byte)(value & 0x7F);
            value >>= 7;
            if ((value == 0 && (b & 0x40) == 0) || (value == -1 && (b & 0x40) != 0))
            {
                _bytes.Add(b);
                return;
            }

            _bytes.Add((byte)(b | 0x80));
        }
    }

    public void Name(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        U32((uint)bytes.Length);
        Bytes(bytes);
    }

    public void Vector(ByteWriter content, int count)
    {
        U32((uint)count);
        Append(content);
    }

    public void Section(byte id, ByteWriter content)
    {
        Byte(id);
        U32((uint)content.Length);
        Append(content);
    }

    public void Section(byte id, ByteWriter content, int count)
    {
        var body = new ByteWriter();
        body.Vector(content, count);
        Section(id, body);
    }

    public byte[] ToArray() => [.. _bytes];
}
