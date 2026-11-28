using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Yaml;
using Meziantou.Framework.Yaml.Nodes;
using Meziantou.Framework.Yaml.Syntax;

namespace Meziantou.Framework.Yaml.Benchmarks;

/// <summary>
/// Measures the main entry points on a realistic document: a list of orders with nested objects, collections,
/// numbers, dates, booleans, multi-line strings and quoted strings.
/// </summary>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded benchmark inputs.")]
public class DocumentBenchmarks : IDisposable
{
    private static readonly YamlSerializerOptions Options = new();

    private Collection<Order> _orders = null!;
    private string _yaml = null!;
    private byte[] _utf8Yaml = null!;
    private readonly MemoryStream _stream = new();
    private YamlTypeInfo<Collection<Order>> _typeInfo = null!;

    [Params(10, 1000)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _orders = CreateOrders(Count);
        _yaml = YamlSerializer.Serialize(_orders, Options);
        _utf8Yaml = Encoding.UTF8.GetBytes(_yaml);
        var context = new BenchmarkYamlContext();
        _typeInfo = (YamlTypeInfo<Collection<Order>>)context.GetTypeInfo(typeof(Collection<Order>), context.Options)!;

        // The source-generated metadata reads and writes the same YAML as reflection
        var expected = YamlSerializer.Serialize(Deserialize(), Options);
        if (!string.Equals(expected, YamlSerializer.Serialize(Deserialize_SourceGenerated(), Options), StringComparison.Ordinal) || !string.Equals(_yaml, Serialize_SourceGenerated(), StringComparison.Ordinal))
            throw new InvalidOperationException("The source-generated metadata does not read or write the same orders");
    }

    [Benchmark]
    public string Serialize() => YamlSerializer.Serialize(_orders, Options);

    [Benchmark]
    public long SerializeToStream()
    {
        _stream.SetLength(0);
        YamlSerializer.Serialize(_stream, _orders, Options);
        return _stream.Length;
    }

    [Benchmark]
    public byte[] SerializeToUtf8Bytes() => YamlSerializer.SerializeToUtf8Bytes(_orders, Options);

    [Benchmark]
    public string Serialize_SourceGenerated() => YamlSerializer.Serialize(_orders, _typeInfo);

    [Benchmark]
    public byte[] SerializeToUtf8Bytes_SourceGenerated() => YamlSerializer.SerializeToUtf8Bytes(_orders, _typeInfo);

    [Benchmark]
    public Collection<Order>? Deserialize_SourceGenerated() => YamlSerializer.Deserialize(_utf8Yaml.AsSpan(), _typeInfo);

    [Benchmark]
    public Collection<Order>? Deserialize() => YamlSerializer.Deserialize<Collection<Order>>(_yaml, Options);

    [Benchmark]
    public Collection<Order>? DeserializeFromStream() => YamlSerializer.Deserialize<Collection<Order>>(new MemoryStream(_utf8Yaml, writable: false), Options);

    // A stream that cannot seek is read by blocks: the locations of the keys are computed as they are read
    [Benchmark]
    public Collection<Order>? DeserializeFromNonSeekableStream()
    {
        using var stream = new NonSeekableStream(new MemoryStream(_utf8Yaml, writable: false));
        return YamlSerializer.Deserialize<Collection<Order>>(stream, Options);
    }

    [Benchmark]
    public Collection<Order>? DeserializeFromUtf8() => YamlSerializer.Deserialize<Collection<Order>>(_utf8Yaml.AsSpan(), Options);

    [Benchmark]
    public int Parse()
    {
        var parser = Parser.CreateParser(new StringReader(_yaml));
        var count = 0;
        while (parser.MoveNext())
        {
            count++;
        }

        return count;
    }

    [Benchmark]
    public YamlStream LoadModel() => YamlStream.Load(new StringReader(_yaml));

    [Benchmark]
    public YamlSyntaxTree ParseSyntaxTree() => YamlSyntaxTree.Parse(_yaml);

    public void Dispose() => _stream.Dispose();

    internal static Collection<Order> CreateOrders(int count)
    {
        var random = new Random(42);
        var result = new Collection<Order>();
        for (var i = 0; i < count; i++)
        {
            var lines = new Collection<OrderLine>();
            var lineCount = random.Next(1, 6);
            for (var j = 0; j < lineCount; j++)
            {
                lines.Add(new OrderLine
                {
                    Sku = "SKU-" + random.Next(100000, 999999).ToString(CultureInfo.InvariantCulture),
                    Description = j % 3 == 0 ? "Item: special, \"quoted\" # not a comment" : "Plain item description number " + j.ToString(CultureInfo.InvariantCulture),
                    Quantity = random.Next(1, 100),
                    UnitPrice = Math.Round((decimal)random.NextDouble() * 1000m, 2, MidpointRounding.AwayFromZero),
                });
            }

            result.Add(new Order
            {
                Id = i,
                Customer = new Customer
                {
                    Name = "Customer " + i.ToString(CultureInfo.InvariantCulture),
                    Email = "customer" + i.ToString(CultureInfo.InvariantCulture) + "@example.com",
                    Address = "1 Main Street\nApartment " + i.ToString(CultureInfo.InvariantCulture) + "\nSpringfield",
                },
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i * 37),
                Shipped = i % 2 == 0,
                Weight = random.NextDouble() * 50,
                Tags = ["tag" + (i % 5).ToString(CultureInfo.InvariantCulture), "priority", "web"],
                Lines = lines,
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["source"] = "web",
                    ["campaign"] = "spring-" + (i % 7).ToString(CultureInfo.InvariantCulture),
                },
            });
        }

        return result;
    }

    public sealed class Order
    {
        public int Id { get; set; }

        public Customer? Customer { get; set; }

        public DateTime CreatedAt { get; set; }

        public bool Shipped { get; set; }

        public double Weight { get; set; }

        public Collection<string>? Tags { get; set; }

        public Collection<OrderLine>? Lines { get; set; }

        public Dictionary<string, string>? Metadata { get; set; }
    }

    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => inner.Read(buffer);

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    public sealed class Customer
    {
        public string? Name { get; set; }

        public string? Email { get; set; }

        public string? Address { get; set; }
    }

    public sealed class OrderLine
    {
        public string? Sku { get; set; }

        public string? Description { get; set; }

        public int Quantity { get; set; }

        public decimal UnitPrice { get; set; }
    }
}
