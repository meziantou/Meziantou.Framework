using System.Text.Unicode;

namespace Meziantou.Framework;

/// <summary>
/// Provides configuration options for slug creation.
/// </summary>
public class SlugOptions
{
    internal static SlugOptions Default { get; } = new SlugOptions();

    /// <summary>
    /// The default maximum length for slugs.
    /// </summary>
    public const int DefaultMaximumLength = 80;

    /// <summary>
    /// The default separator character for slugs.
    /// </summary>
    public const string DefaultSeparator = "-";

    /// <summary>
    /// Gets the list of allowed Unicode character ranges.
    /// </summary>
    public IList<UnicodeRange> AllowedRanges { get; }

    /// <summary>
    /// Gets or sets the maximum length of the slug.
    /// </summary>
    public int MaximumLength { get; set; }

    /// <summary>
    /// Gets or sets the separator to use between words in the slug.
    /// </summary>
    public string Separator { get; set; }

    /// <summary>
    /// Gets or sets the culture to use for character transformations.
    /// </summary>
    public CultureInfo? Culture { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the slug can end with a separator.
    /// </summary>
    public bool CanEndWithSeparator { get; set; }

    /// <summary>
    /// Gets or sets the casing transformation to apply to characters.
    /// </summary>
    public CasingTransformation CasingTransformation { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SlugOptions"/> class with default settings.
    /// </summary>
    public SlugOptions()
    {
        MaximumLength = DefaultMaximumLength;
        Separator = DefaultSeparator;
        AllowedRanges = new List<UnicodeRange>
        {
            UnicodeRange.Create('a', 'z'),
            UnicodeRange.Create('A', 'Z'),
            UnicodeRange.Create('0', '9'),
        };
    }

    /// <summary>
    /// Determines whether the specified character is allowed in the slug.
    /// </summary>
    /// <param name="character">The character to check.</param>
    /// <returns><see langword="true"/> if the character is allowed; otherwise, <see langword="false"/>.</returns>
    public virtual bool IsAllowed(Rune character)
    {
        return AllowedRanges.Count == 0 || AllowedRanges.Any(range => IsInRange(range, character));
    }

    private static bool IsInRange(UnicodeRange range, Rune rune)
    {
        return rune.Value >= range.FirstCodePoint && rune.Value < (range.FirstCodePoint + range.Length);
    }

    /// <summary>
    /// Replaces a rune by applying the configured casing transformation.
    /// </summary>
    /// <param name="rune">The rune to replace.</param>
    /// <returns>The transformed rune as a string.</returns>
    public virtual string Replace(Rune rune)
    {
        if (CasingTransformation == CasingTransformation.ToLowerCase)
        {
            rune = Culture is null ? Rune.ToLowerInvariant(rune) : Rune.ToLower(rune, Culture);
        }
        else if (CasingTransformation == CasingTransformation.ToUpperCase)
        {
            rune = Culture is null ? Rune.ToUpperInvariant(rune) : Rune.ToUpper(rune, Culture);
        }

        return rune.ToString();
    }
}
