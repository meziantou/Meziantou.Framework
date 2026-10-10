namespace Meziantou.Framework.Imaging.TestHarness.Golden;

/// <summary>One decoded text entry (<c>ImageTextEntry</c>).</summary>
/// <param name="Keyword">The keyword.</param>
/// <param name="Value">The text.</param>
/// <param name="LanguageTag">The language tag, or <see langword="null"/>.</param>
/// <param name="TranslatedKeyword">The translated keyword, or <see langword="null"/>.</param>
public sealed record DecodedTextEntry(string Keyword, string Value, string? LanguageTag, string? TranslatedKeyword);
