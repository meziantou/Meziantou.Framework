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

    [JsonSerializable(typeof(CustomPayload))]
    private sealed partial class CustomJsonContext : JsonSerializerContext
    {
    }
}
