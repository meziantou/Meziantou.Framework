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

    // The examples write to the same file, so they run one after the other, and not beside ReadmeExample_BasicExample,
    // which writes trace.json too
    [Fact(DisableParallelization = true)]
    public async Task DocumentationExamples()
    {
        {
            #region ChromiumTracingWriter_Example
            await using var writer = ChromiumTracingWriter.Create("trace.json");
            await writer.WriteEventAsync(new ChromiumTracingCompleteEvent
            {
                Name = "My Operation",
                Category = "category1",
                Timestamp = DateTimeOffset.UtcNow,
                Duration = TimeSpan.FromMilliseconds(150),
                ProcessId = Environment.ProcessId,
                ThreadId = Environment.CurrentManagedThreadId,
            });
            #endregion
        }

        Assert.Equal(["X"], await ReadDocumentationTraceEventTypesAsync());

        {
            #region ChromiumTracingAsyncBeginEvent_Example
            await using var writer = ChromiumTracingWriter.Create("trace.json");
            await writer.WriteEventAsync(new ChromiumTracingAsyncBeginEvent
            {
                Name = "Async Task",
                Category = "async",
                Timestamp = DateTimeOffset.UtcNow,
                Id = 123,
                ProcessId = Environment.ProcessId,
            });

            // The end event has the same Id as the begin event
            await writer.WriteEventAsync(new ChromiumTracingAsyncEndEvent
            {
                Name = "Async Task",
                Category = "async",
                Timestamp = DateTimeOffset.UtcNow,
                Id = 123,
                ProcessId = Environment.ProcessId,
            });
            #endregion
        }

        Assert.Equal(["b", "e"], await ReadDocumentationTraceEventTypesAsync());

        {
            #region ChromiumTracingCompleteEvent_Example
            await using var writer = ChromiumTracingWriter.Create("trace.json");
            var startTime = DateTimeOffset.UtcNow;
            await writer.WriteEventAsync(new ChromiumTracingCompleteEvent
            {
                Name = "Processing",
                Category = "work",
                Timestamp = startTime,
                Duration = TimeSpan.FromMilliseconds(200),
                ProcessId = Environment.ProcessId,
                ThreadId = Environment.CurrentManagedThreadId,
            });
            #endregion
        }

        Assert.Equal(["X"], await ReadDocumentationTraceEventTypesAsync());

        {
            #region ChromiumTracingCounterEvent_Example
            await using var writer = ChromiumTracingWriter.Create("trace.json");
            await writer.WriteEventAsync(new ChromiumTracingCounterEvent
            {
                Name = "Memory Usage",
                Category = "system",
                Timestamp = DateTimeOffset.UtcNow,
                ProcessId = Environment.ProcessId,
                Arguments = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["bytes"] = 1024 * 1024 * 500,
                },
            });
            #endregion
        }

        Assert.Equal(["C"], await ReadDocumentationTraceEventTypesAsync());

        {
            #region ChromiumTracingDurationBeginEvent_Example
            await using var writer = ChromiumTracingWriter.Create("trace.json");
            await writer.WriteEventAsync(new ChromiumTracingDurationBeginEvent
            {
                Name = "Processing",
                Category = "compute",
                Timestamp = DateTimeOffset.UtcNow,
                ProcessId = Environment.ProcessId,
                ThreadId = Environment.CurrentManagedThreadId,
            });

            // The end event closes the last duration event that began on the same thread
            await writer.WriteEventAsync(new ChromiumTracingDurationEndEvent
            {
                Timestamp = DateTimeOffset.UtcNow,
                ProcessId = Environment.ProcessId,
                ThreadId = Environment.CurrentManagedThreadId,
            });
            #endregion
        }

        Assert.Equal(["B", "E"], await ReadDocumentationTraceEventTypesAsync());

        {
            #region ChromiumTracingFlowBeginEvent_Example
            await using var writer = ChromiumTracingWriter.Create("trace.json");
            await writer.WriteEventAsync(new ChromiumTracingFlowBeginEvent
            {
                Name = "Request Flow",
                Category = "network",
                Timestamp = DateTimeOffset.UtcNow,
                ProcessId = Environment.ProcessId,
            });
            #endregion
        }

        Assert.Equal(["s"], await ReadDocumentationTraceEventTypesAsync());

        {
            #region ChromiumTracingInstantEvent_Example
            await using var writer = ChromiumTracingWriter.Create("trace.json");
            await writer.WriteEventAsync(new ChromiumTracingInstantEvent
            {
                Name = "Checkpoint",
                Category = "milestone",
                Timestamp = DateTimeOffset.UtcNow,
                Scope = ChromiumTracingInstantEventScope.Thread,
                ProcessId = Environment.ProcessId,
                ThreadId = Environment.CurrentManagedThreadId,
            });
            #endregion
        }

        Assert.Equal(["i"], await ReadDocumentationTraceEventTypesAsync());

        {
            #region ChromiumTracingMetadataEvent_Example
            await using var writer = ChromiumTracingWriter.Create("trace.json");
            await writer.WriteEventAsync(ChromiumTracingMetadataEvent.ProcessName(
                Environment.ProcessId,
                "My Application"));

            await writer.WriteEventAsync(ChromiumTracingMetadataEvent.ThreadName(
                Environment.ProcessId,
                Environment.CurrentManagedThreadId,
                "Main Thread"));
            #endregion
        }

        Assert.Equal(["M", "M"], await ReadDocumentationTraceEventTypesAsync());

        {
            #region ChromiumTracingObjectCreatedEvent_Example
            await using var writer = ChromiumTracingWriter.Create("trace.json");
            await writer.WriteEventAsync(new ChromiumTracingObjectCreatedEvent
            {
                Name = "MyObject",
                Category = "objects",
                Timestamp = DateTimeOffset.UtcNow,
                Id = "obj-1",
                ProcessId = Environment.ProcessId,
            });
            #endregion
        }

        Assert.Equal(["N"], await ReadDocumentationTraceEventTypesAsync());
    }

    private static async Task<string?[]> ReadDocumentationTraceEventTypesAsync()
    {
        try
        {
            using var document = JsonDocument.Parse(await File.ReadAllBytesAsync("trace.json", XunitCancellationToken));
            return document.RootElement.EnumerateArray().Select(item => item.GetProperty("ph").GetString()).ToArray();
        }
        finally
        {
            File.Delete("trace.json");
        }
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
    public async Task CreateFromAStreamTakesOwnershipOfIt()
    {
        var stream = new MemoryStream();

        await using (var writer = ChromiumTracingWriter.Create(stream))
        {
            await writer.WriteEventAsync(new ChromiumTracingInstantEvent { Name = "sample" });
        }

        Assert.False(stream.CanWrite);
    }

    [Fact]
    public async Task TheConstructorDoesNotTakeOwnershipOfTheStream()
    {
        using var stream = new MemoryStream();

        await using (var writer = new ChromiumTracingWriter(stream))
        {
            await writer.WriteEventAsync(new ChromiumTracingInstantEvent { Name = "sample" });
        }

        Assert.True(stream.CanWrite);
    }

    [Fact]
    public async Task CreateGzipFromAStreamDoesNotTakeOwnershipOfIt()
    {
        using var stream = new MemoryStream();

        await using (var writer = ChromiumTracingWriter.CreateGzip(stream))
        {
            await writer.WriteEventAsync(new ChromiumTracingInstantEvent { Name = "sample" });
        }

        Assert.True(stream.CanWrite);
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

    // Writes trace.json in the current directory, like DocumentationExamples
    [Fact(DisableParallelization = true)]
    public async Task ReadmeExample_BasicExample()
    {
        try
        {
            await WriteTraceAsync();

            using var document = JsonDocument.Parse(await File.ReadAllBytesAsync("trace.json"));
            var tracingEvent = Assert.Single(document.RootElement.EnumerateArray());
            Assert.Equal("My Operation", tracingEvent.GetProperty("name").GetString());
            Assert.Equal("X", tracingEvent.GetProperty("ph").GetString());
            Assert.Equal(42, tracingEvent.GetProperty("args").GetProperty("count").GetInt32());
        }
        finally
        {
            File.Delete("trace.json");
        }

        static async Task WriteTraceAsync()
        {
            #region ChromiumTracing_BasicExample
            // Create a writer
            await using var writer = ChromiumTracingWriter.Create("trace.json");

            // Write a complete event (shows as a box in the timeline)
            await writer.WriteEventAsync(new ChromiumTracingCompleteEvent
            {
                Name = "My Operation",
                Category = "category1",
                Timestamp = DateTimeOffset.UtcNow,
                Duration = TimeSpan.FromMilliseconds(150),
                ProcessId = Environment.ProcessId,
                ThreadId = Environment.CurrentManagedThreadId,
                Arguments = new Dictionary<string, object?>
                {
                    ["result"] = "success",
                    ["count"] = 42,
                },
            });
            #endregion
        }
    }

    [Fact]
    public async Task ReadmeExample_DurationEvents()
    {
        var phases = await GetReadmeExamplePhasesAsync(async writer =>
        {
            #region ChromiumTracing_DurationEvents
            // Begin event
            await writer.WriteEventAsync(new ChromiumTracingDurationBeginEvent
            {
                Name = "Processing",
                Category = "compute",
                Timestamp = DateTimeOffset.UtcNow,
                ProcessId = Environment.ProcessId,
                ThreadId = Environment.CurrentManagedThreadId,
            });

            // ... do work ...

            // End event
            await writer.WriteEventAsync(new ChromiumTracingDurationEndEvent
            {
                Name = "Processing",
                Category = "compute",
                Timestamp = DateTimeOffset.UtcNow,
                ProcessId = Environment.ProcessId,
                ThreadId = Environment.CurrentManagedThreadId,
            });
            #endregion
        });

        Assert.Equal(["B", "E"], phases);
    }

    [Fact]
    public async Task ReadmeExample_CompleteEvents()
    {
        var phases = await GetReadmeExamplePhasesAsync(async writer =>
        {
            #region ChromiumTracing_CompleteEvents
            var startTime = DateTimeOffset.UtcNow;

            // ... do work ...

            await writer.WriteEventAsync(new ChromiumTracingCompleteEvent
            {
                Name = "Complete Operation",
                Category = "work",
                Timestamp = startTime,
                Duration = TimeSpan.FromMilliseconds(200),
                ProcessId = Environment.ProcessId,
                ThreadId = Environment.CurrentManagedThreadId,
            });
            #endregion
        });

        Assert.Equal(["X"], phases);
    }

    [Fact]
    public async Task ReadmeExample_InstantEvents()
    {
        var phases = await GetReadmeExamplePhasesAsync(async writer =>
        {
            #region ChromiumTracing_InstantEvents
            await writer.WriteEventAsync(new ChromiumTracingInstantEvent
            {
                Name = "Checkpoint",
                Category = "milestone",
                Timestamp = DateTimeOffset.UtcNow,
                Scope = ChromiumTracingInstantEventScope.Thread, // Global, Process, or Thread
                ProcessId = Environment.ProcessId,
                ThreadId = Environment.CurrentManagedThreadId,
            });
            #endregion
        });

        Assert.Equal(["i"], phases);
    }

    [Fact]
    public async Task ReadmeExample_CounterEvents()
    {
        var phases = await GetReadmeExamplePhasesAsync(async writer =>
        {
            #region ChromiumTracing_CounterEvents
            await writer.WriteEventAsync(new ChromiumTracingCounterEvent
            {
                Name = "Memory Usage",
                Category = "system",
                Timestamp = DateTimeOffset.UtcNow,
                ProcessId = Environment.ProcessId,
                Arguments = new Dictionary<string, object?>
                {
                    ["bytes"] = 1024 * 1024 * 500, // 500 MB
                },
            });
            #endregion
        });

        Assert.Equal(["C"], phases);
    }

    [Fact]
    public async Task ReadmeExample_AsyncEvents()
    {
        var phases = await GetReadmeExamplePhasesAsync(async writer =>
        {
            #region ChromiumTracing_AsyncEvents
            // Start async operation
            await writer.WriteEventAsync(new ChromiumTracingAsyncBeginEvent
            {
                Name = "Async Task",
                Category = "async",
                Timestamp = DateTimeOffset.UtcNow,
                Id = 123, // Unique ID for this async operation
                ProcessId = Environment.ProcessId,
            });

            // End async operation
            await writer.WriteEventAsync(new ChromiumTracingAsyncEndEvent
            {
                Name = "Async Task",
                Category = "async",
                Timestamp = DateTimeOffset.UtcNow,
                Id = 123, // Same ID as begin event
                ProcessId = Environment.ProcessId,
            });

            // Instant async event
            await writer.WriteEventAsync(new ChromiumTracingAsyncInstantEvent
            {
                Name = "Async Milestone",
                Category = "async",
                Timestamp = DateTimeOffset.UtcNow,
                Id = 123,
                ProcessId = Environment.ProcessId,
            });
            #endregion
        });

        Assert.Equal(["b", "e", "n"], phases);
    }

    [Fact]
    public async Task ReadmeExample_FlowEvents()
    {
        var phases = await GetReadmeExamplePhasesAsync(async writer =>
        {
            #region ChromiumTracing_FlowEvents
            // Start flow
            await writer.WriteEventAsync(new ChromiumTracingFlowBeginEvent
            {
                Name = "Request Flow",
                Category = "network",
                Timestamp = DateTimeOffset.UtcNow,
                ProcessId = Environment.ProcessId,
            });

            // Flow step
            await writer.WriteEventAsync(new ChromiumTracingFlowStepEvent
            {
                Name = "Request Flow",
                Category = "network",
                Timestamp = DateTimeOffset.UtcNow,
                ProcessId = Environment.ProcessId,
            });

            // End flow
            await writer.WriteEventAsync(new ChromiumTracingFlowEndEvent
            {
                Name = "Request Flow",
                Category = "network",
                Timestamp = DateTimeOffset.UtcNow,
                ProcessId = Environment.ProcessId,
            });
            #endregion
        });

        Assert.Equal(["s", "t", "f"], phases);
    }

    [Fact]
    public async Task ReadmeExample_ObjectEvents()
    {
        var phases = await GetReadmeExamplePhasesAsync(async writer =>
        {
            #region ChromiumTracing_ObjectEvents
            // Object created
            await writer.WriteEventAsync(new ChromiumTracingObjectCreatedEvent
            {
                Name = "MyObject",
                Category = "objects",
                Timestamp = DateTimeOffset.UtcNow,
                Id = "obj-1",
                ProcessId = Environment.ProcessId,
            });

            // Object snapshot
            await writer.WriteEventAsync(new ChromiumTracingObjectSnapshotEvent
            {
                Name = "MyObject",
                Category = "objects",
                Timestamp = DateTimeOffset.UtcNow,
                Id = "obj-1",
                ProcessId = Environment.ProcessId,
                Arguments = new Dictionary<string, object?>
                {
                    ["state"] = "active",
                },
            });

            // Object destroyed
            await writer.WriteEventAsync(new ChromiumTracingObjectDestroyedEvent
            {
                Name = "MyObject",
                Category = "objects",
                Timestamp = DateTimeOffset.UtcNow,
                Id = "obj-1",
                ProcessId = Environment.ProcessId,
            });
            #endregion
        });

        Assert.Equal(["N", "O", "D"], phases);
    }

    [Fact]
    public async Task ReadmeExample_MetadataEvents()
    {
        var phases = await GetReadmeExamplePhasesAsync(async writer =>
        {
            #region ChromiumTracing_MetadataEvents
            // Set process name
            await writer.WriteEventAsync(ChromiumTracingMetadataEvent.ProcessName(
                Environment.ProcessId,
                "My Application"));

            // Set process labels
            await writer.WriteEventAsync(ChromiumTracingMetadataEvent.ProcessLabels(
                Environment.ProcessId,
                "label1", "label2"));

            // Set process sort index
            await writer.WriteEventAsync(ChromiumTracingMetadataEvent.ProcessSortIndex(
                Environment.ProcessId,
                1));

            // Set thread name
            await writer.WriteEventAsync(ChromiumTracingMetadataEvent.ThreadName(
                Environment.ProcessId,
                Environment.CurrentManagedThreadId,
                "Main Thread"));

            // Set thread sort index
            await writer.WriteEventAsync(ChromiumTracingMetadataEvent.ThreadSortIndex(
                Environment.ProcessId,
                Environment.CurrentManagedThreadId,
                0));
            #endregion
        });

        Assert.Equal(["M", "M", "M", "M", "M"], phases);
    }

    [Fact]
    public async Task ReadmeExample_MemoryDumpEvents()
    {
        var phases = await GetReadmeExamplePhasesAsync(async writer =>
        {
            #region ChromiumTracing_MemoryDumpEvents
            // Process memory dump
            await writer.WriteEventAsync(new ChromiumTracingMemoryDumpProcessEvent
            {
                Timestamp = DateTimeOffset.UtcNow,
                ProcessId = Environment.ProcessId,
            });

            // Global memory dump
            await writer.WriteEventAsync(new ChromiumTracingMemoryDumpGlobalEvent
            {
                Timestamp = DateTimeOffset.UtcNow,
            });
            #endregion
        });

        Assert.Equal(["v", "V"], phases);
    }

    [Fact]
    public async Task ReadmeExample_OtherEvents()
    {
        var phases = await GetReadmeExamplePhasesAsync(async writer =>
        {
            #region ChromiumTracing_OtherEvents
            // Mark event
            await writer.WriteEventAsync(new ChromiumTracingMarkEvent
            {
                Name = "Mark Point",
                Category = "marks",
                Timestamp = DateTimeOffset.UtcNow,
                ProcessId = Environment.ProcessId,
            });

            // Clock sync event
            await writer.WriteEventAsync(new ChromiumTracingClockSyncEvent
            {
                Timestamp = DateTimeOffset.UtcNow,
                ProcessId = Environment.ProcessId,
            });

            // Context events (for tracking nested scopes)
            await writer.WriteEventAsync(new ChromiumTracingContextBeginEvent
            {
                Name = "Context",
                Category = "context",
                Timestamp = DateTimeOffset.UtcNow,
                ProcessId = Environment.ProcessId,
            });

            await writer.WriteEventAsync(new ChromiumTracingContextEndEvent
            {
                Name = "Context",
                Category = "context",
                Timestamp = DateTimeOffset.UtcNow,
                ProcessId = Environment.ProcessId,
            });
            #endregion
        });

        Assert.Equal(["R", "c", "(", ")"], phases);
    }

    [Fact]
    public async Task ReadmeExample_Compression()
    {
        try
        {
            await WriteTraceAsync();

            using var decompressed = new MemoryStream();
            await using (var gzip = new GZipStream(File.OpenRead("trace.json.gz"), CompressionMode.Decompress))
            {
                await gzip.CopyToAsync(decompressed);
            }

            using var document = JsonDocument.Parse(decompressed.ToArray());
            var tracingEvent = Assert.Single(document.RootElement.EnumerateArray());
            Assert.Equal("Operation", tracingEvent.GetProperty("name").GetString());
        }
        finally
        {
            File.Delete("trace.json.gz");
        }

        static async Task WriteTraceAsync()
        {
            #region ChromiumTracing_Compression
            // Create compressed file
            await using var writer = ChromiumTracingWriter.CreateGzip(
                "trace.json.gz",
                CompressionLevel.Fastest);

            await writer.WriteEventAsync(new ChromiumTracingCompleteEvent
            {
                Name = "Operation",
                Timestamp = DateTimeOffset.UtcNow,
                Duration = TimeSpan.FromMilliseconds(100),
            });
            #endregion
        }
    }

    // Runs a readme example that writes events to the writer it is given, and returns the phases of the written events
    private static async Task<List<string?>> GetReadmeExamplePhasesAsync(Func<ChromiumTracingWriter, Task> writeEvents)
    {
        using var stream = new MemoryStream();
        await using (var writer = ChromiumTracingWriter.Create(stream, streamOwned: false))
        {
            await writeEvents(writer);
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.EnumerateArray().Select(element => element.GetProperty("ph").GetString()).ToList();
    }

    private static List<Type> GetConcreteEventTypes()
    {
        return typeof(ChromiumTracingWriter).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && type.IsAssignableTo(typeof(ChromiumTracingEvent)))
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToList();
    }

    // Not a positional record: the System.Text.Json source generator reaches an init accessor through an extern
    // [UnsafeAccessor] method, which does not compile under the updated memory safety rules (dotnet/runtime#133592).
    private sealed record CustomPayload(int Value)
    {
        public int Value { get; } = Value;
    }

    private sealed class UnserializableArgument;

    [JsonSerializable(typeof(CustomPayload))]
    [JsonSourceGenerationOptions(RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
    private sealed partial class CustomJsonContext : JsonSerializerContext
    {
    }
}
