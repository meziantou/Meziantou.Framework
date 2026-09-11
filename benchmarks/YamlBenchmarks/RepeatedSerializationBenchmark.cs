using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Yaml;

namespace YamlBenchmarks;

/// <summary>
/// Measures repeated calls with the same options instance. The reflection contract of a type is built once per
/// options instance, so the second and later calls must not pay for rebuilding it.
/// </summary>
[MemoryDiagnoser]
public class RepeatedSerializationBenchmark
{
    private static readonly YamlSerializerOptions Options = new();

    private readonly Payload _payload = new() { Name = "name", Count = 42, Enabled = true };
    private readonly string _yaml = "Name: name\nCount: 42\nEnabled: true\n";

    [Params(1, 100)]
    public int Iterations { get; set; }

    [Benchmark]
    public string Serialize()
    {
        var result = string.Empty;
        for (var i = 0; i < Iterations; i++)
        {
            result = YamlSerializer.Serialize(_payload, Options);
        }

        return result;
    }

    [Benchmark]
    public Payload? Deserialize()
    {
        Payload? result = null;
        for (var i = 0; i < Iterations; i++)
        {
            result = YamlSerializer.Deserialize<Payload>(_yaml, Options);
        }

        return result;
    }

    public sealed class Payload
    {
        public string? Name { get; set; }

        public int Count { get; set; }

        public bool Enabled { get; set; }
    }
}
