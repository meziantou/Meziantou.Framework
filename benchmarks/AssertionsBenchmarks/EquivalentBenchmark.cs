using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Assertions;

namespace AssertionsBenchmarks;

/// <summary>Measures the success path of the structural comparison used by <c>Assert.Equivalent</c>.</summary>
[MemoryDiagnoser]
public class EquivalentBenchmark
{
    private readonly string _expectedText = "Meziantou";
    private readonly string _actualText = new(['M', 'e', 'z', 'i', 'a', 'n', 't', 'o', 'u']);

    private Customer _expectedCustomer = null!;
    private Customer _actualCustomer = null!;
    private int[] _expectedArray = null!;
    private int[] _actualArray = null!;

    [GlobalSetup]
    public void Setup()
    {
        _expectedCustomer = CreateCustomer();
        _actualCustomer = CreateCustomer();
        _expectedArray = Enumerable.Range(0, 100).ToArray();
        _actualArray = Enumerable.Range(0, 100).ToArray();

        static Customer CreateCustomer() => new()
        {
            Id = 42,
            Name = "Meziantou",
            CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Address = new Address { Street = "1 rue de la Paix", City = "Paris", ZipCode = "75000" },
            Tags = ["a", "b", "c"],
        };
    }

    [Benchmark]
    public void EquivalentScalar() => Assert.Equivalent(42, 42);

    [Benchmark]
    public void EquivalentString() => Assert.Equivalent(_expectedText, _actualText);

    [Benchmark]
    public void EquivalentObject() => Assert.Equivalent(_expectedCustomer, _actualCustomer);

    [Benchmark]
    public void EquivalentCollection() => Assert.Equivalent(_expectedArray, _actualArray);

    private sealed class Customer
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public DateTime CreatedAt { get; set; }
        public Address? Address { get; set; }
        public string[]? Tags { get; set; }
    }

    private sealed class Address
    {
        public string? Street { get; set; }
        public string? City { get; set; }
        public string? ZipCode { get; set; }
    }
}
