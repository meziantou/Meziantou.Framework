using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace SpecGen;

internal sealed class GeneratorContext(string root, bool check)
{
    private readonly List<string> _outdatedFiles = [];

    public string Root { get; } = root;

    public string SpecData(params string[] path) => Path.Combine([Root, "tests", "Meziantou.Framework.WebPlatform.SpecData", .. path]);

    public string Source(params string[] path) => Path.Combine([Root, "src", "Meziantou.Framework.WebPlatform", .. path]);

    public void WriteFile(string path, string content)
    {
        content = content.ReplaceLineEndings("\n");
        if (File.Exists(path) && string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
            return;

        if (check)
        {
            _outdatedFiles.Add(Path.GetRelativePath(Root, path));
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Console.WriteLine($"Updated {Path.GetRelativePath(Root, path)}");
    }

    /// <summary>
    /// Writes a binary file compressed with Brotli: the length of the content (32-bit little-endian), then the Brotli stream.
    /// An existing file is compared after decompression, so that another version of the encoder does not make it outdated.
    /// </summary>
    public void WriteCompressedFile(string path, byte[] content)
    {
        if (File.Exists(path) && ReadCompressedFile(File.ReadAllBytes(path)) is { } existing && existing.AsSpan().SequenceEqual(content))
            return;

        if (check)
        {
            _outdatedFiles.Add(Path.GetRelativePath(Root, path));
            return;
        }

        var compressed = new byte[4 + BrotliEncoder.GetMaxCompressedLength(content.Length)];
        BinaryPrimitives.WriteInt32LittleEndian(compressed, content.Length);
        if (!BrotliEncoder.TryCompress(content, compressed.AsSpan(4), out var written, quality: 11, window: 24))
            throw new InvalidOperationException($"Cannot compress {path}");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, compressed.AsSpan(0, 4 + written).ToArray());
        Console.WriteLine($"Updated {Path.GetRelativePath(Root, path)}");

        static byte[]? ReadCompressedFile(byte[] data)
        {
            if (data.Length < 4)
                return null;

            var content = new byte[BinaryPrimitives.ReadInt32LittleEndian(data)];
            return BrotliDecoder.TryDecompress(data.AsSpan(4), content, out var written) && written == content.Length ? content : null;
        }
    }

    public void DeleteFile(string path)
    {
        if (!File.Exists(path))
            return;

        if (check)
        {
            _outdatedFiles.Add(Path.GetRelativePath(Root, path));
            return;
        }

        File.Delete(path);
        Console.WriteLine($"Deleted {Path.GetRelativePath(Root, path)}");
    }

    public int Complete()
    {
        if (_outdatedFiles.Count == 0)
            return 0;

        foreach (var file in _outdatedFiles)
        {
            Console.Error.WriteLine($"Generated file is out of date: {file}");
        }

        Console.Error.WriteLine("Run 'dotnet run --project tools/Meziantou.Framework.WebPlatform.SpecGen' and commit the result.");
        return 1;
    }
}

internal static class RepositoryPaths
{
    public static string FindRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "Meziantou.Framework.slnx")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        return directory ?? throw new InvalidOperationException("Cannot find the repository root");
    }
}
