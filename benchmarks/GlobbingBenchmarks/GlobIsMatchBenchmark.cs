#nullable disable
using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Globbing;

namespace GlobbingBenchmarks;

//[MemoryDiagnoser]
[ReturnValueValidator]
[MarkdownExporterAttribute.Default]
public class GlobIsMatchBenchmark
{
    private Glob _meziantouGlob;

    [Params([
        "*.txt",
        "**/*.txt",
        "file*.txt",
        "**/file*.txt",
        "folder[0-1]/**/f{ab,il}[aei]*.{txt,png,ico}",
    ])]
    public string Pattern { get; set; }

    [Params(
    [
        "test.txt",
        "file0001.txt",
        "file00000000000001.txt",
        "test01/test02/test03/test04/file0001.txt",
    ])]
    public string Path { get; set; }

    [GlobalSetup]
    public void Initialize()
    {
        _meziantouGlob = Glob.Parse(Pattern, GlobOptions.None);
    }

    [Benchmark]
    public bool Meziantou_Globbing() => _meziantouGlob.IsMatch(Path);
}
