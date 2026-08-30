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
    public void SettingTheClockSyncNameToNullIsANoOp()
    {
        var tracingEvent = new ChromiumTracingClockSyncEvent { Name = null };

        Assert.Equal("clock_sync", tracingEvent.Name);
    }

    [Fact]
    public void SettingTheClockSyncNameToItsOwnValueIsANoOp()
    {
        var tracingEvent = new ChromiumTracingClockSyncEvent { Name = "clock_sync" };

        Assert.Equal("clock_sync", tracingEvent.Name);
    }

    [Fact]
    public void SettingTheClockSyncNameToAnotherValueThrows()
    {
        Assert.Throws<ArgumentException>(() => new ChromiumTracingClockSyncEvent { Name = "other" });
    }

    [Fact]
    public void CopyingTheNameBetweenEventsWorksForEveryEventType()
    {
        // Name is virtual on the base type, so anything that copies it across events must not break on a single type
        foreach (var eventType in typeof(ChromiumTracingWriter).Assembly.GetTypes().Where(type => !type.IsAbstract && type.IsAssignableTo(typeof(ChromiumTracingEvent))))
        {
            var source = (ChromiumTracingEvent)Activator.CreateInstance(eventType)!;
            var destination = (ChromiumTracingEvent)Activator.CreateInstance(eventType)!;

            destination.Name = source.Name;

            Assert.Equal(source.Name, destination.Name);
        }
    }

    private sealed record CustomPayload(int Value);

    [JsonSerializable(typeof(CustomPayload))]
    private sealed partial class CustomJsonContext : JsonSerializerContext
    {
    }
}
