using BenchmarkDotNet.Attributes;

namespace Meziantou.Framework.SimpleQueryLanguage.Benchmarks;

[MemoryDiagnoser]
public class EvaluateBenchmarks
{
    private Issue[] _issues = null!;
    private Query<Issue> _query = null!;

    [Params("Single", "Typical", "Mixed", "Long", "Or", "Range")]
    public string Query { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        _issues = BenchmarkData.CreateIssues(1000);
        _query = BenchmarkData.CreateQueryBuilder().Build(BenchmarkData.Queries[Query]);
    }

    [Benchmark]
    public int Evaluate()
    {
        var count = 0;
        foreach (var issue in _issues)
        {
            if (_query.Evaluate(issue))
            {
                count++;
            }
        }

        return count;
    }
}
