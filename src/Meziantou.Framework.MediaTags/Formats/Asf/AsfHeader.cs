using System.Buffers.Binary;
using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags.Formats.Asf;

/// <summary>The header object of an ASF file, read into memory.</summary>
internal sealed class AsfHeader
{
    /// <summary>The largest header read into memory. It holds the pictures, but not the audio or video.</summary>
    public const int MaxSize = 64 * 1024 * 1024;

    /// <summary>The header object GUID, size, object count and two reserved bytes.</summary>
    public const int PrefixSize = 30;

    private AsfHeader(byte[] bytes, List<AsfObject> children)
    {
        Bytes = bytes;
        Children = children;
    }

    public byte[] Bytes { get; }

    public List<AsfObject> Children { get; }

    /// <summary>Reads the header object at the start of the stream.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="header">The header.</param>
    /// <param name="complete"><see langword="true"/> when its objects cover the whole header, which a writer requires.</param>
    /// <param name="error">Why the header cannot be read.</param>
    public static bool TryRead(Stream stream, [NotNullWhen(true)] out AsfHeader? header, out bool complete, out string? error)
    {
        header = null;
        complete = false;
        error = null;

        stream.Position = 0;
        Span<byte> prefix = stackalloc byte[PrefixSize];
        if (stream.ReadAtLeast(prefix, prefix.Length, throwOnEndOfStream: false) < prefix.Length || new Guid(prefix[..16]) != AsfGuids.Header)
        {
            error = "The file is not an ASF file.";
            return false;
        }

        var size = BinaryPrimitives.ReadUInt64LittleEndian(prefix[16..]);
        if (size < PrefixSize || size > (ulong)stream.Length)
        {
            error = "The ASF header object declares a size that runs past the end of the file.";
            return false;
        }

        if (size > MaxSize)
        {
            error = "The ASF header object is too large to read.";
            return false;
        }

        stream.Position = 0;
        if (!StreamHelpers.TryReadExact(stream, (long)size, MaxSize, out var bytes))
        {
            error = "The file ended inside the ASF header object.";
            return false;
        }

        complete = AsfObject.TryParseList(bytes.AsMemory(PrefixSize), out var children);
        header = new AsfHeader(bytes, children);
        return true;
    }
}
