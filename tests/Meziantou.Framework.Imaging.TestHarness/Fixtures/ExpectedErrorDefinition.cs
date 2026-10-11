using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>The expected failure of an invalid, unsupported or limit fixture.</summary>
public sealed class ExpectedErrorDefinition
{
    /// <summary>Gets the expected exception type name (e.g. <c>InvalidImageContentException</c>).</summary>
    [JsonPropertyName("exception")]
    public required string Exception { get; init; }

    /// <summary>Gets the expected value of the exception <c>Format</c> property (an <c>ImageFormat</c> member name).</summary>
    [JsonPropertyName("format")]
    public string? Format { get; init; }

    /// <summary>Gets the expected value of the exception <c>Feature</c> property.</summary>
    [JsonPropertyName("feature")]
    public string? Feature { get; init; }

    /// <summary>Gets the expected value of the exception <c>Kind</c> property (an <c>ImageResourceLimitKind</c> member name).</summary>
    [JsonPropertyName("limitKind")]
    public string? LimitKind { get; init; }
}
