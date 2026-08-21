using BenchmarkDotNet.Attributes;
using Meziantou.Framework.InlineSnapshotTesting;

namespace SnapshotTestingBenchmarks;

/// <summary>Measures an inline snapshot assertion whose value already matches, which is what a passing test does.</summary>
[MemoryDiagnoser]
public class InlineSnapshotBenchmark
{
    private readonly object _value = new { FirstName = "John", LastName = "Doe", Age = 42 };
    private InlineSnapshotSettings _settings = null!;
    private string _expected = null!;

    [GlobalSetup]
    public void Setup()
    {
        _settings = InlineSnapshotSettings.Default with { };
        _expected = _settings.SnapshotSerializer.Serialize(_value) ?? "";
    }

    [Benchmark]
    public void Validate() => InlineSnapshot.Validate(_value, _settings, _expected, filePath: "Benchmark.cs", lineNumber: 1);
}
