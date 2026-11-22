using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection;

namespace Meziantou.Framework;

/// <summary>
/// Reads the embedded resources the build compresses, the <c>EmbeddedResource</c> items with <c>Compression="Deflate"</c>
/// (see <c>eng/CompressedEmbeddedResources.targets</c>): the length of the content (32-bit little-endian), then the content
/// compressed with raw Deflate, which every runtime decompresses, WebAssembly included.
/// </summary>
internal static class CompressedResource
{
    /// <summary>Opens the content of a compressed resource, or returns <see langword="null"/> when the assembly has no such resource.</summary>
    public static Stream? TryOpen(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name);
        if (stream is null)
            return null;

        Span<byte> header = stackalloc byte[4];
        stream.ReadExactly(header);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 0)
            throw new InvalidDataException($"The resource '{name}' is not a compressed resource.");

        var content = new byte[length];
        using (var deflate = new DeflateStream(stream, CompressionMode.Decompress))
        {
            deflate.ReadExactly(content);
            if (deflate.ReadByte() >= 0)
                throw new InvalidDataException($"The resource '{name}' is longer than its header says.");
        }

        return new MemoryStream(content, writable: false);
    }

    /// <summary>Opens the content of a compressed resource.</summary>
    /// <exception cref="InvalidOperationException">The assembly has no such resource.</exception>
    public static Stream Open(Assembly assembly, string name) => TryOpen(assembly, name) ?? throw new InvalidOperationException($"The resource '{name}' is missing.");
}
