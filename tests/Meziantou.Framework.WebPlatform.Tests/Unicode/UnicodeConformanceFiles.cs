using System.IO.Compression;
using System.Text;

namespace Meziantou.Framework.WebPlatform.Tests.Unicode;

/// <summary>
/// The conformance test files of the Unicode Character Database, the ones of the version of Meziantou.Framework.Unicode
/// (tests/Meziantou.Framework.Unicode.Tests/files, embedded compressed), so they always match the version of the data.
/// </summary>
internal static class UnicodeConformanceFiles
{
    public static IEnumerable<string> ReadLines(string name)
    {
        using var stream = typeof(UnicodeConformanceFiles).Assembly.GetManifestResourceStream("unicode/" + name + ".gz")
            ?? throw new InvalidOperationException($"The resource 'unicode/{name}.gz' is not embedded");
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }
}
