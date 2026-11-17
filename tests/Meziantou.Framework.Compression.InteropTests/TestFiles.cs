using System.Security.Cryptography;
using System.Text;

namespace Meziantou.Framework.Compression.InteropTests;

/// <summary>Creates directories of test files, and compares the trees that the tools and the library extract.</summary>
internal static class TestFiles
{
    public static TemporaryFolder CreateTemporaryFolder() => new(Directory.CreateTempSubdirectory("mfc-interop-").FullName);

    /// <summary>
    /// Writes a directory with text, binary (a pseudo-random part and an x86-like part), empty and nested files, so that
    /// every method and filter has data to work on.
    /// </summary>
    public static string CreateSourceTree(string root)
    {
        var source = Path.Combine(root, "source");
        Directory.CreateDirectory(Path.Combine(source, "docs", "nested"));
        Directory.CreateDirectory(Path.Combine(source, "empty-directory"));

        var text = new StringBuilder();
        for (var i = 0; i < 3000; i++)
        {
            text.Append("Line ").Append(i).Append(": the quick brown fox jumps over the lazy dog, ").Append(i * 31 % 97).Append('\n');
        }

        File.WriteAllText(Path.Combine(source, "readme.txt"), text.ToString());
        File.WriteAllText(Path.Combine(source, "docs", "nested", "unicode-é日本.txt"), "Unicode name\n");
        File.WriteAllBytes(Path.Combine(source, "empty.bin"), []);
        File.WriteAllBytes(Path.Combine(source, "random.bin"), CreatePseudoRandom(150_000, seed: 1));
        File.WriteAllBytes(Path.Combine(source, "app.exe"), CreateExecutableLike(120_000));
        return source;
    }

    /// <summary>Gets the SHA-256 of every file of a tree, by path relative to the root with <c>/</c> separators, and the directories.</summary>
    public static SortedDictionary<string, string> GetTree(string root)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
            if (Directory.Exists(path))
            {
                result[relative] = "<directory>";
                continue;
            }

            // An archive can store a file without the read permission of its owner
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserRead);
            }

            result[relative] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        }

        return result;
    }

    public static byte[] CreatePseudoRandom(int length, uint seed)
    {
        // xorshift32: System.Random is rejected by the analyzers (CA5394), and the data must be reproducible
        var data = new byte[length];
        var state = seed is 0 ? 2463534242u : seed;
        for (var i = 0; i < data.Length; i++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            data[i] = (byte)state;
        }

        return data;
    }

    private static byte[] CreateExecutableLike(int length)
    {
        // Relative CALL (E8) and JMP (E9) instructions to nearby targets, between runs of repeated code: what BCJ x86 converts
        var data = new byte[length];
        var noise = CreatePseudoRandom(length, seed: 7);
        for (var i = 0; i + 5 <= length; i += 16)
        {
            data[i] = (i / 16) % 3 is 0 ? (byte)0xE9 : (byte)0xE8;
            BitConverter.TryWriteBytes(data.AsSpan(i + 1, 4), 0x1000 - i);
            data[i + 5] = 0x55;
            data[i + 6] = 0x8B;
            data[i + 7] = 0xEC;
            data[i + 8] = noise[i];
        }

        return data;
    }
}
