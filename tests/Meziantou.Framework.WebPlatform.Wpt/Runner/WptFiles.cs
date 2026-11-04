using System.Collections.Concurrent;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>
/// The resources of the test suite shared by the tests (e.g. testharness.js and the .headers files), read once: the files do
/// not change while the tests run, and reading and decoding them again for each test allocates a lot.
/// </summary>
internal static class WptFiles
{
    private static readonly ConcurrentDictionary<string, string> Texts = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string[]?> HeadersFiles = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, bool> AsciiFiles = new(StringComparer.Ordinal);

    /// <summary>Returns the content of a file, decoded as <see cref="File.ReadAllText(string)"/> does.</summary>
    public static string ReadAllText(string path) => Texts.GetOrAdd(path, File.ReadAllText);

    /// <summary>
    /// Returns whether a file only contains ASCII bytes, so that it has the same text in the ASCII-compatible encodings. A file
    /// that contains a NUL byte is not: it is likely encoded in UTF-16 (e.g. the ASCII characters of a UTF-16 file without byte
    /// order mark, decoded with the charset attribute of a script).
    /// </summary>
    public static bool IsAscii(string path) => AsciiFiles.GetOrAdd(path, static path =>
    {
        var bytes = File.ReadAllBytes(path);
        return System.Text.Ascii.IsValid(bytes) && !bytes.AsSpan().Contains((byte)0);
    });

    /// <summary>Returns the lines of a .headers file, or null when it does not exist.</summary>
    public static string[]? ReadHeadersFile(string path) => HeadersFiles.GetOrAdd(path, static path => File.Exists(path) ? File.ReadAllLines(path) : null);
}
