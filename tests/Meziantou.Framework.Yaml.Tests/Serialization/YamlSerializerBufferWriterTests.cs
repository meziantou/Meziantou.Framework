using System.Buffers;
using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlSerializerBufferWriterTests
{
    [Fact]
    public void Serialize_IBufferWriter_ShouldWriteYaml()
    {
        var writer = new ArrayBufferWriter<char>();

        YamlSerializer.Serialize(writer, new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 1 });

        var yaml = new string(writer.WrittenSpan);
        Assert.Contains("a:", yaml);
        Assert.Contains("1", yaml);
    }

    [Fact]
    public void Serialize_IBufferWriter_WithContext_ShouldWriteYaml()
    {
        var writer = new ArrayBufferWriter<char>();
        var context = new TestYamlSerializerContext();

        YamlSerializer.Serialize(writer, new GeneratedPerson { FirstName = "Bob", Age = 42 }, context);

        var yaml = new string(writer.WrittenSpan);
        Assert.Contains("first_name", yaml);
        Assert.Contains("Bob", yaml);
        Assert.Contains("Age", yaml);
    }

    [Fact]
    public void BufferWriterTextWriter_Reset_ShouldWriteToNewDestination()
    {
        var first = new ArrayBufferWriter<char>();
        var second = new ArrayBufferWriter<char>();

        using var writer = new BufferWriterTextWriter(first);
        writer.Write("abc");

        writer.Reset(second);
        writer.Write("def");

        Assert.Equal("abc", new string(first.WrittenSpan));
        Assert.Equal("def", new string(second.WrittenSpan));
    }

    [Fact]
    public void BufferWriterTextWriter_Reset_WithNullDestination_ShouldThrow()
    {
        using var writer = new BufferWriterTextWriter(new ArrayBufferWriter<char>());

        Assert.Throws<ArgumentNullException>(() => writer.Reset(destination: null!));
    }

    [Fact]
    public void BufferWriterTextWriter_UseAfterReturnedToCache_ShouldThrow()
    {
        var writer = BufferWriterTextWriterCache.RentWriter(new ArrayBufferWriter<char>());
        BufferWriterTextWriterCache.ReturnWriter(writer);

        Assert.Throws<ObjectDisposedException>(() => writer.Write('a'));
        Assert.Throws<ObjectDisposedException>(() => writer.Write("abc"));
    }

    [Fact]
    public void BufferWriterTextWriterCache_ShouldReuseTheSameInstance()
    {
        var first = BufferWriterTextWriterCache.RentWriter(new ArrayBufferWriter<char>());
        BufferWriterTextWriterCache.ReturnWriter(first);

        var second = BufferWriterTextWriterCache.RentWriter(new ArrayBufferWriter<char>());
        BufferWriterTextWriterCache.ReturnWriter(second);

        Assert.Same(first, second);
    }

    [Fact]
    public void BufferWriterTextWriterCache_NestedRent_ShouldUseDistinctInstances()
    {
        var outerDestination = new ArrayBufferWriter<char>();
        var innerDestination = new ArrayBufferWriter<char>();

        var outer = BufferWriterTextWriterCache.RentWriter(outerDestination);
        var inner = BufferWriterTextWriterCache.RentWriter(innerDestination);
        try
        {
            Assert.NotSame(outer, inner);

            outer.Write("outer");
            inner.Write("inner");
        }
        finally
        {
            BufferWriterTextWriterCache.ReturnWriter(inner);
            BufferWriterTextWriterCache.ReturnWriter(outer);
        }

        Assert.Equal("outer", new string(outerDestination.WrittenSpan));
        Assert.Equal("inner", new string(innerDestination.WrittenSpan));

        var reused = BufferWriterTextWriterCache.RentWriter(new ArrayBufferWriter<char>());
        BufferWriterTextWriterCache.ReturnWriter(reused);
        Assert.Same(outer, reused);
    }

    [Fact]
    public void Serialize_IBufferWriter_ConsecutiveCalls_ShouldWriteToTheirOwnDestination()
    {
        var first = new ArrayBufferWriter<char>();
        var second = new ArrayBufferWriter<char>();

        YamlSerializer.Serialize(first, new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 1 });
        YamlSerializer.Serialize(second, new Dictionary<string, int>(StringComparer.Ordinal) { ["b"] = 2 });

        Assert.Equal("a: 1\n", new string(first.WrittenSpan));
        Assert.Equal("b: 2\n", new string(second.WrittenSpan));
    }

    [Fact]
    public void Serialize_IBufferWriter_WhenSerializationThrows_ShouldReleaseTheCachedWriter()
    {
        var cached = BufferWriterTextWriterCache.RentWriter(new ArrayBufferWriter<char>());
        BufferWriterTextWriterCache.ReturnWriter(cached);

        var destination = new ArrayBufferWriter<char>();
        Assert.ThrowsAny<Exception>(() => YamlSerializer.Serialize(destination, new ThrowingValue()));

        var afterFailure = BufferWriterTextWriterCache.RentWriter(new ArrayBufferWriter<char>());
        BufferWriterTextWriterCache.ReturnWriter(afterFailure);

        Assert.Same(cached, afterFailure);
    }

    private sealed class ThrowingValue
    {
        public string Message { get; } = "boom";

        public int Value => throw new InvalidOperationException(Message);
    }
}
