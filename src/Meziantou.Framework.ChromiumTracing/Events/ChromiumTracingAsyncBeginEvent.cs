using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

/// <summary>Represents the beginning of an asynchronous operation. Must be paired with a <see cref="ChromiumTracingAsyncEndEvent"/> with the same Id.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.ChromiumTracing.Tests/WriterTests.cs" ref:region="ChromiumTracingAsyncBeginEvent_Example" ref:dedent="true">
/// await using var writer = ChromiumTracingWriter.Create("trace.json");
/// await writer.WriteEventAsync(new ChromiumTracingAsyncBeginEvent
/// {
///     Name = "Async Task",
///     Category = "async",
///     Timestamp = DateTimeOffset.UtcNow,
///     Id = 123,
///     ProcessId = Environment.ProcessId,
/// });
///
/// // The end event has the same Id as the begin event
/// await writer.WriteEventAsync(new ChromiumTracingAsyncEndEvent
/// {
///     Name = "Async Task",
///     Category = "async",
///     Timestamp = DateTimeOffset.UtcNow,
///     Id = 123,
///     ProcessId = Environment.ProcessId,
/// });
/// </code>
/// </example>
public sealed class ChromiumTracingAsyncBeginEvent : ChromiumTracingAsyncEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "b";
}
