using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>An expected text entry (library <c>ImageTextEntry</c>); empty PNG language tags and translated keywords are <see langword="null"/>.</summary>
public sealed class TextEntryExpectation
{
    /// <summary>Gets the keyword (GIF comments and JPEG COM segments use <c>Comment</c>).</summary>
    [JsonPropertyName("keyword")]
    public required string Keyword { get; init; }

    /// <summary>Gets the text.</summary>
    [JsonPropertyName("value")]
    public required string Value { get; init; }

    /// <summary>Gets the language tag, or <see langword="null"/>.</summary>
    [JsonPropertyName("languageTag")]
    public required string? LanguageTag { get; init; }

    /// <summary>Gets the translated keyword, or <see langword="null"/>.</summary>
    [JsonPropertyName("translatedKeyword")]
    public required string? TranslatedKeyword { get; init; }

    /// <inheritdoc/>
    public override string ToString() => $"{Keyword}{(LanguageTag is null ? "" : "[" + LanguageTag + "]")}: {Value}";
}
