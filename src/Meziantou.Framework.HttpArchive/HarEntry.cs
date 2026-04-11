using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meziantou.Framework.HttpArchive;

/// <summary>Represents an exported HTTP request.</summary>
public sealed class HarEntry
{
    /// <summary>Gets or sets the reference to the parent page. Leave out if the application does not support grouping by pages.</summary>
    [JsonPropertyName("pageref")]
    public string? Pageref { get; set; }

    /// <summary>Gets or sets the date and time stamp of the request start.</summary>
    [JsonPropertyName("startedDateTime")]
    public DateTimeOffset StartedDateTime { get; set; }

    /// <summary>Gets or sets the total elapsed time of the request in milliseconds.</summary>
    [JsonPropertyName("time")]
    public double Time { get; set; }

    /// <summary>Gets or sets the detailed info about the request.</summary>
    [JsonPropertyName("request")]
    public HarRequest Request { get; set; } = new();

    /// <summary>Gets or sets the detailed info about the response.</summary>
    [JsonPropertyName("response")]
    public HarResponse Response { get; set; } = new();

    /// <summary>Gets or sets the info about cache usage.</summary>
    [JsonPropertyName("cache")]
    public HarCache Cache { get; set; } = new();

    /// <summary>Gets or sets the detailed timing info about request/response round trip.</summary>
    [JsonPropertyName("timings")]
    public HarTimings Timings { get; set; } = new();

    /// <summary>Gets or sets the IP address of the server that was connected.</summary>
    [JsonPropertyName("serverIPAddress")]
    public string? ServerIPAddress { get; set; }

    /// <summary>Gets or sets the unique ID of the parent TCP/IP connection.</summary>
    [JsonPropertyName("connection")]
    public string? Connection { get; set; }

    /// <summary>Gets or sets a comment provided by the user or the application.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    /// <summary>Gets or sets additional vendor-specific fields.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
