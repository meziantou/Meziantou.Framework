using System.IO.Compression;
using System.Reflection;

namespace Meziantou.Framework;

/// <summary>
/// Reads the embedded resources the build compresses, the <c>EmbeddedResource</c> items with <c>Compression="Deflate"</c>
/// (see <c>eng/CompressedEmbeddedResources.targets</c>): a zip archive whose only entry is the content compressed with
/// Deflate, which every runtime decompresses, WebAssembly included.
/// </summary>
internal static class CompressedResource
{
    /// <summary>Opens the content of a compressed resource, or returns <see langword="null"/> when the assembly has no such resource.</summary>
    public static Stream? TryOpen(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name);
        if (stream is null)
            return null;

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        if (archive.Entries is not [var entry])
            throw new InvalidDataException($"The resource '{name}' is not a compressed resource.");

        var content = new byte[checked((int)entry.Length)];
        using (var entryStream = entry.Open())
        {
            entryStream.ReadExactly(content);
        }

        return new MemoryStream(content, writable: false);
    }

    /// <summary>Opens the content of a compressed resource.</summary>
    /// <exception cref="InvalidOperationException">The assembly has no such resource.</exception>
    public static Stream Open(Assembly assembly, string name) => TryOpen(assembly, name) ?? throw new InvalidOperationException($"The resource '{name}' is missing.");
}
