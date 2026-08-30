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
