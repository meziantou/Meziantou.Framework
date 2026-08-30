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

    [JsonSerializable(typeof(CustomPayload))]
    private sealed partial class CustomJsonContext : JsonSerializerContext
    {
    }
}
