using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

/// <summary>Represents an object creation event.</summary>
/// <example>
/// <code>
/// await writer.WriteEventAsync(new ChromiumTracingObjectCreatedEvent
/// {
///     Name = "MyObject",
///     Category = "objects",
///     Timestamp = DateTimeOffset.UtcNow,
///     Id = "obj-1",
///     ProcessId = Environment.ProcessId
/// });
/// </code>
/// </example>
public sealed class ChromiumTracingObjectCreatedEvent : ChromiumTracingObjectEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "N";
}
