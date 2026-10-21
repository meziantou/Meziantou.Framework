using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

/// <summary>Represents an object creation event.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.ChromiumTracing.Tests/WriterTests.cs" ref:region="ChromiumTracingObjectCreatedEvent_Example" ref:dedent="true">
/// await using var writer = ChromiumTracingWriter.Create("trace.json");
/// await writer.WriteEventAsync(new ChromiumTracingObjectCreatedEvent
/// {
///     Name = "MyObject",
///     Category = "objects",
///     Timestamp = DateTimeOffset.UtcNow,
///     Id = "obj-1",
///     ProcessId = Environment.ProcessId,
/// });
/// </code>
/// </example>
public sealed class ChromiumTracingObjectCreatedEvent : ChromiumTracingObjectEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "N";
}
