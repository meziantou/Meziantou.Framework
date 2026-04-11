using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meziantou.Framework.HttpArchive;

/// <summary>Represents the detailed timing info about request/response round trip.</summary>
public sealed class HarTimings
{
    /// <summary>Gets or sets the time spent in a queue waiting for a network connection, in milliseconds. Use -1 if the timing does not apply.</summary>
    [JsonPropertyName("blocked")]
    public double? Blocked { get; set; }

    /// <summary>Gets or sets the DNS resolution time, in milliseconds. Use -1 if the timing does not apply.</summary>
    [JsonPropertyName("dns")]
    public double? Dns { get; set; }

    /// <summary>Gets or sets the time required to create TCP connection, in milliseconds. Use -1 if the timing does not apply.</summary>
    [JsonPropertyName("connect")]
    public double? Connect { get; set; }

    /// <summary>Gets or sets the time required to send HTTP request to the server, in milliseconds.</summary>
    [JsonPropertyName("send")]
    public double Send { get; set; }

    /// <summary>Gets or sets the waiting for a response from the server, in milliseconds.</summary>
    [JsonPropertyName("wait")]
    public double Wait { get; set; }

    /// <summary>Gets or sets the time required to read entire response from the server (or cache), in milliseconds.</summary>
    [JsonPropertyName("receive")]
    public double Receive { get; set; }

    /// <summary>Gets or sets the time required for SSL/TLS negotiation, in milliseconds. Use -1 if the timing does not apply.</summary>
    [JsonPropertyName("ssl")]
    public double? Ssl { get; set; }

    /// <summary>Gets or sets a comment provided by the user or the application.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    /// <summary>Gets or sets additional vendor-specific fields.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
