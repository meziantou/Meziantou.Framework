using BenchmarkDotNet.Attributes;
using Meziantou.Framework.SimpleQueryLanguage.Syntax;

namespace Meziantou.Framework.SimpleQueryLanguage.Benchmarks;

[MemoryDiagnoser]
public class BuildBenchmarks
{
    private QueryBuilder<Issue> _queryBuilder = null!;
    private ExpressionQueryBuilder<Issue> _expressionQueryBuilder = null!;
    private string _query = null!;

    [Params("Single", "Typical", "Mixed", "Long", "Or", "Range")]
    public string Query { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        _queryBuilder = BenchmarkData.CreateQueryBuilder();
        _expressionQueryBuilder = BenchmarkData.CreateExpressionQueryBuilder();
        _query = BenchmarkData.Queries[Query];
    }

    [Benchmark]
    public QuerySyntax Parse() => QuerySyntax.Parse(_query);

    [Benchmark]
    public Query<Issue> Build() => _queryBuilder.Build(_query);

    [Benchmark]
    public ExpressionQuery<Issue> BuildExpression() => _expressionQueryBuilder.Build(_query);
}
