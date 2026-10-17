using System.Numerics;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Dom.FileApi;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.Storage;

/// <summary>The encodings of the keys (<see cref="IdbKey"/>) and of the values serialized for storage (<see cref="StructuredValueEncoding"/>).</summary>
public sealed class StorageEncodingTests
{
    private static readonly IdbKey[] SortedKeys =
    [
        IdbKey.FromNumber(double.NegativeInfinity),
        IdbKey.FromNumber(-1e300),
        IdbKey.FromNumber(-1),
        IdbKey.FromNumber(-double.Epsilon),
        IdbKey.FromNumber(0),
        IdbKey.FromNumber(double.Epsilon),
        IdbKey.FromNumber(1),
        IdbKey.FromNumber(1.5),
        IdbKey.FromNumber(double.PositiveInfinity),
        IdbKey.FromDate(-1),
        IdbKey.FromDate(0),
        IdbKey.FromDate(1e12),
        IdbKey.FromString(""),
        IdbKey.FromString("\0"),
        IdbKey.FromString("\0\0"),
        IdbKey.FromString("\u0001"),
        IdbKey.FromString("a"),
        IdbKey.FromString("a\0"),
        IdbKey.FromString("ab"),
        IdbKey.FromString("ÿ"),
        IdbKey.FromString("Ā"),
        IdbKey.FromString("\uD800"),
        IdbKey.FromString("￿"),
        IdbKey.FromBinary([]),
        IdbKey.FromBinary([0]),
        IdbKey.FromBinary([0, 0]),
        IdbKey.FromBinary([0, 255]),
        IdbKey.FromBinary([1]),
        IdbKey.FromBinary([255]),
        IdbKey.FromArray([]),
        IdbKey.FromArray([IdbKey.FromNumber(1)]),
        IdbKey.FromArray([IdbKey.FromNumber(1), IdbKey.FromNumber(2)]),
        IdbKey.FromArray([IdbKey.FromString("a")]),
        IdbKey.FromArray([IdbKey.FromArray([])]),
    ];

    [Fact]
    public void Keys_ByteOrderIsTheKeyOrder()
    {
        for (var i = 0; i < SortedKeys.Length; i++)
        {
            for (var j = 0; j < SortedKeys.Length; j++)
            {
                Assert.Equal(Math.Sign(i.CompareTo(j)), IdbKey.Compare(SortedKeys[i].Encode(), SortedKeys[j].Encode()));
            }
        }
    }

    [Fact]
    public void Keys_RoundTrip()
    {
        foreach (var key in SortedKeys)
        {
            var decoded = IdbKey.Decode(key.Encode());
            Assert.Equal(key.ToString(), decoded.ToString());
            Assert.Equal(key, decoded);
        }
    }

    [Fact]
    public void Keys_NegativeZeroIsZero()
    {
        Assert.Equal(IdbKey.FromNumber(0), IdbKey.FromNumber(-0.0));
        Assert.True(double.IsPositive(IdbKey.Decode(IdbKey.FromNumber(-0.0).Encode()).Number));
    }

    [Fact]
    public void Keys_InvalidBytes_ThrowFormatException()
    {
        Assert.Throws<FormatException>(() => IdbKey.Decode([]));
        Assert.Throws<FormatException>(() => IdbKey.Decode([0x10, 1, 2]));
        Assert.Throws<FormatException>(() => IdbKey.Decode([0x30, 0, 1]));
        Assert.Throws<FormatException>(() => IdbKey.Decode([0x50]));
        Assert.Throws<FormatException>(() => IdbKey.Decode([0x99]));
    }

    [Fact]
    public void Values_RoundTripWithSharedAndCyclicReferences()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var record = new ExportedRecord();
        var array = new object?[] { bytes, bytes, record, "\uD800", 1.5, new BigInteger(-12345678901234567L), true, null, UndefinedValue.Instance };
        record.Fields.Add(new("self", record));
        record.Fields.Add(new("items", array));

        var decoded = StructuredValueEncoding.Decode(StructuredValueEncoding.Encode(new AgentSerializedValue(record)));
        var root = Assert.IsType<ExportedRecord>(decoded.Root);
        Assert.Same(root, root.Fields[0].Value);
        var items = Assert.IsType<object?[]>(root.Fields[1].Value);
        Assert.Equal(bytes, Assert.IsType<byte[]>(items[0]));
        Assert.Same(items[0], items[1]);
        Assert.Same(root, items[2]);
        Assert.Equal("\uD800", items[3]);
        Assert.Equal(1.5, items[4]);
        Assert.Equal(new BigInteger(-12345678901234567L), items[5]);
        Assert.Equal(true, items[6]);
        Assert.Null(items[7]);
        Assert.Same(UndefinedValue.Instance, items[8]);
    }

    [Fact]
    public void Values_RoundTripThePlatformObjects()
    {
        var file = Meziantou.Framework.WebPlatform.Dom.FileApi.File.Create([1, 2], "text/plain", "a.txt", 1234);
        var objects = new object?[]
        {
            new ExportedPlatformObject(Blob.Create([5, 6, 7], "application/octet-stream")),
            new ExportedPlatformObject(file),
            new ExportedPlatformObject(new DomException("message", "DataError")),
            new ExportedPlatformObject(new DomMatrix([1, 2, 3, 4, 5, 6])),
            new ExportedPlatformObject(new DomPointReadOnly(1, 2, 3, 4)),
        };

        var decoded = Assert.IsType<object?[]>(StructuredValueEncoding.Decode(StructuredValueEncoding.Encode(new AgentSerializedValue(objects))).Root);
        var blob = Assert.IsType<Blob>(Assert.IsType<ExportedPlatformObject>(decoded[0]).Value);
        Assert.Equal([5, 6, 7], blob.Data.ToArray());
        Assert.Equal("application/octet-stream", blob.Type);
        var decodedFile = Assert.IsType<Meziantou.Framework.WebPlatform.Dom.FileApi.File>(Assert.IsType<ExportedPlatformObject>(decoded[1]).Value);
        Assert.Equal(("a.txt", 1234L, "text/plain"), (decodedFile.Name, decodedFile.LastModified, decodedFile.Type));
        var exception = Assert.IsType<DomException>(Assert.IsType<ExportedPlatformObject>(decoded[2]).Value);
        Assert.Equal(("message", "DataError"), (exception.Message, exception.Name));
        var matrix = Assert.IsType<DomMatrix>(Assert.IsType<ExportedPlatformObject>(decoded[3]).Value);
        Assert.Equal((true, 5.0, 6.0), (matrix.Is2D, matrix.E, matrix.F));
        var point = Assert.IsType<DomPointReadOnly>(Assert.IsType<ExportedPlatformObject>(decoded[4]).Value);
        Assert.Equal((1.0, 2.0, 3.0, 4.0), (point.X, point.Y, point.Z, point.W));
    }

    [Fact]
    public void Values_UnsupportedFormat_ThrowFormatException()
    {
        Assert.Throws<FormatException>(() => StructuredValueEncoding.Decode([]));
        Assert.Throws<FormatException>(() => StructuredValueEncoding.Decode([2, 0]));
        Assert.Throws<FormatException>(() => StructuredValueEncoding.Decode([1, 11, 5]));
    }
}
