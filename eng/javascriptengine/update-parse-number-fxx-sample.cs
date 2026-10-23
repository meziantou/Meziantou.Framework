#!/usr/bin/env dotnet
#:sdk Meziantou.NET.Sdk
// Writes tests/Meziantou.Framework.JavaScriptEngine.Tests/Corpus/ParseNumberFxx.txt.gz, the sample of
// parse-number-fxx-test-data that the normal test run checks. Fetch the corpus first:
//   dotnet run eng/javascriptengine/fetch-tests.cs parse-number-fxx-test-data
// The sample is deterministic: the small hand-picked files in full, a fixed stride of the large generated files, and the
// first line of every power of two (the numbers whose rounding interval is asymmetric).
using System.IO.Compression;
using System.Text;

var root = FindRoot(Environment.CurrentDirectory) ?? FindRoot(AppContext.BaseDirectory) ?? throw new InvalidOperationException("Cannot find the root of the repository (Meziantou.Framework.slnx)");
var dataPath = Path.Combine(root, "tests", "Meziantou.Framework.JavaScriptEngine.External", "parse-number-fxx-test-data", "data");
var outputPath = Path.Combine(root, "tests", "Meziantou.Framework.JavaScriptEngine.Tests", "Corpus", "ParseNumberFxx.txt.gz");
if (!Directory.Exists(dataPath))
    throw new InvalidOperationException($"{dataPath} does not exist. Run 'dotnet run eng/javascriptengine/fetch-tests.cs parse-number-fxx-test-data'.");

var lines = new List<string>();
var seenLines = new HashSet<string>(StringComparer.Ordinal);
var seenPowersOfTwo = new HashSet<string>(StringComparer.Ordinal);
foreach (var file in Directory.GetFiles(dataPath, "*.txt").Order(StringComparer.Ordinal))
{
    var name = Path.GetFileNameWithoutExtension(file);
    var stride = name switch
    {
        "freetype-2-7" or "google-wuffs" or "lemire-fast-float" or "more-test-cases" or "tencent-rapidjson" => 1,
        _ when name.StartsWith("remyoudompheng-fptest-", StringComparison.Ordinal) => 2000,
        _ => 200,
    };

    var index = 0;
    foreach (var line in File.ReadLines(file))
    {
        // The bits of the float64 are the third field: a power of two has a zero significand
        var f64 = line.Split(' ')[2];
        var isPowerOfTwo = f64.AsSpan(3).IndexOfAnyExcept('0') < 0 && seenPowersOfTwo.Add(f64);
        if ((index++ % stride == 0 || isPowerOfTwo) && seenLines.Add(line))
        {
            lines.Add(line);
        }
    }
}

using (var output = File.Create(outputPath))
using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize))
using (var writer = new StreamWriter(gzip, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
{
    writer.NewLine = "\n";
    foreach (var line in lines)
    {
        writer.WriteLine(line);
    }
}

Console.WriteLine($"Wrote {lines.Count} lines to {outputPath} ({new FileInfo(outputPath).Length} bytes)");
return 0;

static string? FindRoot(string directory)
{
    for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
    {
        if (File.Exists(Path.Combine(current.FullName, "Meziantou.Framework.slnx")))
            return current.FullName;
    }

    return null;
}
