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

    /// <summary>Returns whether a file only contains ASCII bytes, so that it has the same text in the ASCII-compatible encodings.</summary>
    public static bool IsAscii(string path) => AsciiFiles.GetOrAdd(path, static path => System.Text.Ascii.IsValid(File.ReadAllBytes(path)));

    /// <summary>Returns the lines of a .headers file, or null when it does not exist.</summary>
    public static string[]? ReadHeadersFile(string path) => HeadersFiles.GetOrAdd(path, static path => File.Exists(path) ? File.ReadAllLines(path) : null);
}
