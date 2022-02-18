using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Globbing;

namespace GlobbingBenchmarks;

//[MemoryDiagnoser]
[ReturnValueValidator]
[MarkdownExporterAttribute.Default]
public class GlobIsMatchBenchmark
{
    private Glob _meziantouGlob;
    private GlobExpressions.Glob _globExpressions;
    private GlobExpressions.Glob _globExpressionsCompiled;
    private DotNet.Globbing.Glob _dotnetGlob;

    [Params(new object[]{
        "*.txt",
        "**/*.txt",
        "file*.txt",
        "**/file*.txt",
        "folder[0-1]/**/f{ab,il}[aei]*.{txt,png,ico}",
    })]
    public string Pattern { get; set; }

    [Params(new object[]
    {
        "test.txt",
        "file0001.txt",
        "file00000000000001.txt",
        "test01/test02/test03/test04/file0001.txt",
    })]
    public string Path { get; set; }

    [GlobalSetup]
    public void Initialize()
    {
        _meziantouGlob = Glob.Parse(Pattern, GlobOptions.None);
        _globExpressions = new GlobExpressions.Glob(Pattern, GlobExpressions.GlobOptions.MatchFullPath);
        _globExpressionsCompiled = new GlobExpressions.Glob(Pattern, GlobExpressions.GlobOptions.MatchFullPath | GlobExpressions.GlobOptions.Compiled);
        _dotnetGlob = DotNet.Globbing.Glob.Parse(Pattern);
    }

    [Benchmark]
    public bool Meziantou_Globbing() => _meziantouGlob.IsMatch(Path);

    [Benchmark]
    public bool GlobExpressions_None() => _globExpressions.IsMatch(Path);

    [Benchmark]
    public bool GlobExpressions_Compiled() => _globExpressionsCompiled.IsMatch(Path);

    [Benchmark]
    public bool DotNetGlobbing() => _dotnetGlob.IsMatch(Path);
}
