using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.MediaTags.Formats.Asf;

/// <summary>An object of an ASF header: a GUID, a 64-bit size, and the content.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct AsfObject(Guid Id, ReadOnlyMemory<byte> Bytes)
{
    public const int HeaderSize = 24;

    public ReadOnlyMemory<byte> Data => Bytes[HeaderSize..];

    /// <summary>Splits the content of a header or header extension object into its objects.</summary>
    /// <returns><see langword="false"/> when the objects do not cover the whole content.</returns>
    public static bool TryParseList(ReadOnlyMemory<byte> data, out List<AsfObject> objects)
    {
        objects = [];
        var span = data.Span;
        var offset = 0;
        while (offset < span.Length)
        {
            if (span.Length - offset < HeaderSize)
                return false;

            var size = BinaryPrimitives.ReadUInt64LittleEndian(span[(offset + 16)..]);
            if (size < HeaderSize || size > (ulong)(span.Length - offset))
                return false;

            objects.Add(new AsfObject(new Guid(span.Slice(offset, 16)), data.Slice(offset, (int)size)));
            offset += (int)size;
        }

        return true;
    }

    public static byte[] Build(Guid id, ReadOnlySpan<byte> data)
    {
        var result = new byte[HeaderSize + data.Length];
        id.TryWriteBytes(result);
        BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(16), (ulong)result.Length);
        data.CopyTo(result.AsSpan(HeaderSize));
        return result;
    }
}
