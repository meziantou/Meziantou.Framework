using System.IO.Compression;
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
    public async Task EveryEventTypeIsWrittenWithItsDocumentedPhase()
    {
        // The phase codes and property names are the wire format: they must not change silently.
        var timestamp = new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.Zero);

        using var stream = new MemoryStream();
        await using (var writer = ChromiumTracingWriter.Create(stream, streamOwned: false))
        {
            foreach (var eventType in GetConcreteEventTypes())
            {
                var instance = (ChromiumTracingEvent)Activator.CreateInstance(eventType)!;
                instance.Category = "category";
                instance.Timestamp = timestamp;
                instance.ProcessId = 1;
                instance.ThreadId = 2;
                if (instance is not ChromiumTracingClockSyncEvent)
                {
                    instance.Name = eventType.Name;
                }

                await writer.WriteEventAsync(instance);
            }
        }

        var expectedTimestamp = timestamp.UtcTicks / 10;
        var expectedPhases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [nameof(ChromiumTracingAsyncBeginEvent)] = "b",
            [nameof(ChromiumTracingAsyncEndEvent)] = "e",
            [nameof(ChromiumTracingAsyncInstantEvent)] = "n",
            [nameof(ChromiumTracingClockSyncEvent)] = "c",
            [nameof(ChromiumTracingCompleteEvent)] = "X",
            [nameof(ChromiumTracingContextBeginEvent)] = "(",
            [nameof(ChromiumTracingContextEndEvent)] = ")",
            [nameof(ChromiumTracingCounterEvent)] = "C",
            [nameof(ChromiumTracingDurationBeginEvent)] = "B",
            [nameof(ChromiumTracingDurationEndEvent)] = "E",
            [nameof(ChromiumTracingFlowBeginEvent)] = "s",
            [nameof(ChromiumTracingFlowEndEvent)] = "f",
            [nameof(ChromiumTracingFlowStepEvent)] = "t",
            [nameof(ChromiumTracingInstantEvent)] = "i",
            [nameof(ChromiumTracingLinkIdEvent)] = "=",
            [nameof(ChromiumTracingMarkEvent)] = "R",
            [nameof(ChromiumTracingMemoryDumpGlobalEvent)] = "V",
            [nameof(ChromiumTracingMemoryDumpProcessEvent)] = "v",
            [nameof(ChromiumTracingMetadataEvent)] = "M",
            [nameof(ChromiumTracingObjectCreatedEvent)] = "N",
            [nameof(ChromiumTracingObjectDestroyedEvent)] = "D",
            [nameof(ChromiumTracingObjectSnapshotEvent)] = "O",
        };

        using var document = JsonDocument.Parse(stream.ToArray());
        var elements = document.RootElement.EnumerateArray().ToList();
        Assert.HasCount(expectedPhases.Count, elements);

        // A new event type must be added to the expected map above rather than silently skipped
        Assert.HasCount(expectedPhases.Count, GetConcreteEventTypes());

        foreach (var element in elements)
        {
            var name = element.TryGetProperty("name", out var nameProperty) ? nameProperty.GetString() : null;
            var typeName = name == "clock_sync" ? nameof(ChromiumTracingClockSyncEvent) : name;

            Assert.NotNull(typeName);
            Assert.Equal(expectedPhases[typeName], element.GetProperty("ph").GetString());
            Assert.Equal("category", element.GetProperty("cat").GetString());
            Assert.Equal(expectedTimestamp, element.GetProperty("ts").GetInt64());
            Assert.Equal(1, element.GetProperty("pid").GetInt32());
            Assert.Equal(2, element.GetProperty("tid").GetInt32());
        }
    }

    [Fact]
    public async Task AnEmptyTraceIsAnEmptyJsonArray()
    {
        using var stream = new MemoryStream();
        await using (var writer = ChromiumTracingWriter.Create(stream, streamOwned: false))
        {
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.Equal(0, document.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task AGzipTraceRoundTrips()
    {
        using var stream = new MemoryStream();
        await using (var writer = ChromiumTracingWriter.CreateGzip(stream))
        {
            await writer.WriteEventAsync(new ChromiumTracingInstantEvent { Name = "compressed", ProcessId = 1 });
        }

        stream.Position = 0;
        using var decompressed = new MemoryStream();
        using (var gzip = new GZipStream(stream, CompressionMode.Decompress))
        {
            await gzip.CopyToAsync(decompressed);
        }

        using var document = JsonDocument.Parse(decompressed.ToArray());
        Assert.Equal(1, document.RootElement.GetArrayLength());
        Assert.Equal("compressed", document.RootElement[0].GetProperty("name").GetString());
    }

    private static List<Type> GetConcreteEventTypes()
    {
        return typeof(ChromiumTracingWriter).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && type.IsAssignableTo(typeof(ChromiumTracingEvent)))
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToList();
    }

    private sealed record CustomPayload(int Value);

    private sealed class UnserializableArgument;

    [JsonSerializable(typeof(CustomPayload))]
    private sealed partial class CustomJsonContext : JsonSerializerContext
    {
    }
}
