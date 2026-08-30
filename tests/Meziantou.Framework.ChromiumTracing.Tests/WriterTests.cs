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

    private sealed record CustomPayload(int Value);

    private sealed class UnserializableArgument;

    [JsonSerializable(typeof(CustomPayload))]
    private sealed partial class CustomJsonContext : JsonSerializerContext
    {
    }
}
