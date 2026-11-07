using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

/// <summary>Represents the beginning of a duration event. Must be paired with a <see cref="ChromiumTracingDurationEndEvent"/>.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.ChromiumTracing.Tests/WriterTests.cs" ref:region="ChromiumTracingDurationBeginEvent_Example" ref:dedent="true">
/// await using var writer = ChromiumTracingWriter.Create("trace.json");
/// await writer.WriteEventAsync(new ChromiumTracingDurationBeginEvent
/// {
///     Name = "Processing",
///     Category = "compute",
///     Timestamp = DateTimeOffset.UtcNow,
///     ProcessId = Environment.ProcessId,
///     ThreadId = Environment.CurrentManagedThreadId,
/// });
///
/// // The end event closes the last duration event that began on the same thread
/// await writer.WriteEventAsync(new ChromiumTracingDurationEndEvent
/// {
///     Timestamp = DateTimeOffset.UtcNow,
///     ProcessId = Environment.ProcessId,
///     ThreadId = Environment.CurrentManagedThreadId,
/// });
/// </code>
/// </example>
public sealed class ChromiumTracingDurationBeginEvent : ChromiumTracingDurationEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "B";
}
