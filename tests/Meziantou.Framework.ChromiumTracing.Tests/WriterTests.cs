using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing.Tests;

public sealed partial class WriterTests
{
    [Fact]
    public async Task WriteEvents()
    {
        await using var writer = ChromiumTracingWriter.Create(Stream.Null);

        var eventTypes = typeof(ChromiumTracingWriter).Assembly.GetTypes().Where(t => !t.IsAbstract && t.IsAssignableTo(typeof(ChromiumTracingEvent)));
        foreach (var eventType in eventTypes)
        {
            var instance = Activator.CreateInstance(eventType) as ChromiumTracingEvent;
            Assert.NotNull(instance);
            await writer.WriteEventAsync(instance);
        }

        await writer.WriteEventAsync(ChromiumTracingMetadataEvent.ThreadName(1, 2, "name"));
        await writer.WriteEventAsync(ChromiumTracingMetadataEvent.ThreadSortIndex(1, 2, 3));

        // Custom writes
        await writer.WriteEventAsync(new ChromiumTracingInstantEvent
        {
            Name = "Sample",
            Category = "category",
            Timestamp = DateTimeOffset.UtcNow,
            Scope = ChromiumTracingInstantEventScope.Thread,
            ProcessId = 1,
            ThreadId = 2,
            ColorName = "yellow",
            Arguments = new Dictionary<string, object?>(StringComparer.Ordinal) { ["step"] = "sample" },
        });

        // Custom writes
        await writer.WriteEventAsync(new ChromiumTracingCompleteEvent
        {
            Name = "sample",
            Category = "category",
            Timestamp = DateTimeOffset.UtcNow,
            Duration = TimeSpan.FromSeconds(1),
            ProcessId = 1,
            ThreadId = 2,
        });
    }

    [Fact]
    public async Task WriteEventsWithInt64Arguments()
    {
        await using var writer = ChromiumTracingWriter.Create(Stream.Null);

        await writer.WriteEventAsync(new ChromiumTracingInstantEvent
        {
            Name = "Sample",
            Timestamp = DateTimeOffset.UtcNow,
            Arguments = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["value"] = 123L,
            },
        });
    }

    [Fact]
    public async Task WriteEventsWithCustomSerializerContext()
    {
        await using var writer = ChromiumTracingWriter.Create(Stream.Null, CustomJsonContext.Default);

        await writer.WriteEventAsync(new ChromiumTracingInstantEvent
        {
            Name = "Sample",
            Timestamp = DateTimeOffset.UtcNow,
            Arguments = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["payload"] = new CustomPayload(42),
            },
        });
    }

    [Fact]
    public async Task WriteEventAsyncThrowsWhenTheEventIsNull()
    {
        await using var writer = ChromiumTracingWriter.Create(Stream.Null);

        await Assert.ThrowsAsync<ArgumentNullException>(() => writer.WriteEventAsync(tracingEvent: null!));
    }

    [Fact]
    public async Task AFailedWriteKeepsTheDocumentValid()
    {
        using var stream = new MemoryStream();
        await using (var writer = ChromiumTracingWriter.Create(stream, streamOwned: false))
        {
            await writer.WriteEventAsync(new ChromiumTracingInstantEvent { Name = "before" });

            await Assert.ThrowsAsync<NotSupportedException>(() => writer.WriteEventAsync(new ChromiumTracingInstantEvent
            {
                Name = "unserializable",
                Arguments = new Dictionary<string, object?>(StringComparer.Ordinal) { ["value"] = new UnserializableArgument() },
            }));

            await writer.WriteEventAsync(new ChromiumTracingInstantEvent { Name = "after" });
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        Assert.Equal(2, document.RootElement.GetArrayLength());
        Assert.Equal("before", document.RootElement[0].GetProperty("name").GetString());
        Assert.Equal("after", document.RootElement[1].GetProperty("name").GetString());
    }

    [Fact]
    public async Task AFailedFirstWriteKeepsTheDocumentValid()
    {
        using var stream = new MemoryStream();
        await using (var writer = ChromiumTracingWriter.Create(stream, streamOwned: false))
        {
            await Assert.ThrowsAsync<NotSupportedException>(() => writer.WriteEventAsync(new ChromiumTracingInstantEvent
            {
                Name = "unserializable",
                Arguments = new Dictionary<string, object?>(StringComparer.Ordinal) { ["value"] = new UnserializableArgument() },
            }));
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        Assert.Equal(0, document.RootElement.GetArrayLength());
    }

    public static TheoryData<object> SupportedArgumentValues() => new()
    {
        "text",
        (bool)true,
        (byte)1,
        (sbyte)1,
        (char)'c',
        (short)1,
        (ushort)1,
        (int)1,
        (uint)1,
        (long)1,
        (ulong)1,
        (float)1.5f,
        (double)1.5,
        (decimal)1.5m,
        Guid.Empty,
        new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc),
        new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.Zero),
        TimeSpan.FromSeconds(1),
        new Uri("https://example.com"),
    };

    [Theory]
    [MemberData(nameof(SupportedArgumentValues))]
    public async Task ArgumentValuesOfCommonTypesCanBeSerialized(object value)
    {
        using var stream = new MemoryStream();
        await using (var writer = ChromiumTracingWriter.Create(stream, streamOwned: false))
        {
            await writer.WriteEventAsync(new ChromiumTracingInstantEvent
            {
                Name = "Sample",
                Arguments = new Dictionary<string, object?>(StringComparer.Ordinal) { ["value"] = value },
            });
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        Assert.Equal(1, document.RootElement.GetArrayLength());
        Assert.True(document.RootElement[0].GetProperty("args").TryGetProperty("value", out _));
    }

    [Fact]
    public async Task ConcurrentWritesProduceAValidDocument()
    {
        const int EventCount = 500;

        using var stream = new MemoryStream();
        await using (var writer = ChromiumTracingWriter.Create(stream, streamOwned: false))
        {
            await Parallel.ForEachAsync(Enumerable.Range(0, EventCount), async (index, cancellationToken) =>
            {
                await writer.WriteEventAsync(new ChromiumTracingInstantEvent
                {
                    Name = "event",
                    Category = "category",
                    ProcessId = 1,
                    ThreadId = index,
                }, cancellationToken);
            });
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        Assert.Equal(EventCount, document.RootElement.GetArrayLength());

        var threadIds = document.RootElement.EnumerateArray().Select(element => element.GetProperty("tid").GetInt32()).ToList();
        Assert.Equal(Enumerable.Range(0, EventCount), threadIds.Order());
    }

    [Fact]
    public async Task DisposingTwiceDoesNotWriteTheClosingBracketTwice()
    {
        using var stream = new MemoryStream();
        var writer = ChromiumTracingWriter.Create(stream, streamOwned: false);
        await writer.WriteEventAsync(new ChromiumTracingInstantEvent { Name = "sample" });

        await writer.DisposeAsync();
        await writer.DisposeAsync();

        using var document = JsonDocument.Parse(stream.ToArray());
        Assert.Equal(1, document.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task WritingAfterDisposeThrows()
    {
        using var stream = new MemoryStream();
        var writer = ChromiumTracingWriter.Create(stream, streamOwned: false);
        await writer.WriteEventAsync(new ChromiumTracingInstantEvent { Name = "sample" });
        await writer.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => writer.WriteEventAsync(new ChromiumTracingInstantEvent { Name = "after" }));

        using var document = JsonDocument.Parse(stream.ToArray());
        Assert.Equal(1, document.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task TheOwnedStreamIsDisposedWhenTheClosingBracketCannotBeWritten()
    {
        using var stream = new FailingStream();

        await Assert.ThrowsAsync<IOException>(async () =>
        {
            await using var writer = ChromiumTracingWriter.Create(stream, streamOwned: true);
        });

        Assert.True(stream.Disposed);
    }

    private sealed class FailingStream : Stream
    {
        public bool Disposed { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("The stream cannot be written to");

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => throw new IOException("The stream cannot be written to");

        public override ValueTask DisposeAsync()
        {
            Disposed = true;
            return base.DisposeAsync();
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    [Fact]
    public async Task TheTimestampDoesNotDependOnTheOffset()
    {
        var utc = new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.Zero);

        using var stream = new MemoryStream();
        await using (var writer = ChromiumTracingWriter.Create(stream, streamOwned: false))
        {
            await writer.WriteEventAsync(new ChromiumTracingInstantEvent { Name = "utc", Timestamp = utc, ThreadTimestamp = utc });
            await writer.WriteEventAsync(new ChromiumTracingInstantEvent { Name = "positive", Timestamp = utc.ToOffset(TimeSpan.FromHours(2)), ThreadTimestamp = utc.ToOffset(TimeSpan.FromHours(2)) });
            await writer.WriteEventAsync(new ChromiumTracingInstantEvent { Name = "negative", Timestamp = utc.ToOffset(TimeSpan.FromHours(-5)), ThreadTimestamp = utc.ToOffset(TimeSpan.FromHours(-5)) });
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        var timestamps = document.RootElement.EnumerateArray().Select(element => element.GetProperty("ts").GetInt64()).ToList();
        var threadTimestamps = document.RootElement.EnumerateArray().Select(element => element.GetProperty("tts").GetInt64()).ToList();

        Assert.Equal(utc.UtcTicks / 10, timestamps[0]);
        Assert.Equal(new[] { timestamps[0], timestamps[0], timestamps[0] }, timestamps);
        Assert.Equal(new[] { timestamps[0], timestamps[0], timestamps[0] }, threadTimestamps);
    }

    private sealed record CustomPayload(int Value);

    private sealed class UnserializableArgument;

    [JsonSerializable(typeof(CustomPayload))]
    private sealed partial class CustomJsonContext : JsonSerializerContext
    {
    }
}
