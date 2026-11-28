using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;

namespace Meziantou.Framework.Yaml.Benchmarks;

/// <summary>Measures the deserialization of collections of value types, whose elements are read by a typed converter.</summary>
[MemoryDiagnoser]
public class CollectionBenchmarks
{
    private static readonly YamlSerializerOptions Options = new();

    private byte[] _integers = null!;
    private byte[] _numbers = null!;
    private byte[] _dictionary = null!;

    [Params(10_000)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _integers = Encoding.UTF8.GetBytes(YamlSerializer.Serialize(Enumerable.Range(0, Count).ToList(), Options));
        _numbers = Encoding.UTF8.GetBytes(YamlSerializer.Serialize(Enumerable.Range(0, Count).Select(i => i / 7.0).ToList(), Options));
        _dictionary = Encoding.UTF8.GetBytes(YamlSerializer.Serialize(Enumerable.Range(0, Count).ToDictionary(i => "key" + i.ToString(CultureInfo.InvariantCulture), i => i), Options));
    }

    [Benchmark]
    public int DeserializeList() => YamlSerializer.Deserialize<List<int>>(_integers, Options)!.Count;

    [Benchmark]
    public int DeserializeArray() => YamlSerializer.Deserialize<int[]>(_integers, Options)!.Length;

    [Benchmark]
    public int DeserializeCollection() => YamlSerializer.Deserialize<Collection<double>>(_numbers, Options)!.Count;

    [Benchmark]
    public int DeserializeDictionary() => YamlSerializer.Deserialize<Dictionary<string, int>>(_dictionary, Options)!.Count;
}
