using System.Collections.Concurrent;
using System.Text.Unicode;

namespace Meziantou.Framework;

/// <summary>
/// Provides options for customizing slug generation.
/// <example>
/// <code>
/// var options = new SlugOptions
/// {
///     MaximumLength = 20,
///     CasingTransformation = CasingTransformation.ToLowerCase,
/// };
/// var slug = Slug.Create("This is a text", options); // this-is-a-text
/// </code>
/// </example>
/// </summary>
public class SlugOptions
{
    internal static SlugOptions Default { get; } = new SlugOptions();

    /// <summary>Caches, per derived type, whether <see cref="Replace(Rune)"/> is still the one declared here.</summary>
    private static readonly ConcurrentDictionary<Type, bool> UsesDefaultReplaceByType = new();

    /// <summary>The default maximum length for generated slugs (80 characters).</summary>
    public const int DefaultMaximumLength = 80;

    /// <summary>The default separator used between words ("-").</summary>
    public const string DefaultSeparator = "-";

    /// <summary>Gets the list of allowed Unicode character ranges in the generated slug.</summary>
    public IList<UnicodeRange> AllowedRanges { get; }

    /// <summary>
    /// Gets or sets the maximum length of the generated slug. Default is 80. A value less than or equal to zero means the slug is not truncated.
    /// </summary>
    /// <remarks>
    /// The limit applies to the returned slug and is never exceeded. A slug is only cut between characters, so it never
    /// ends with an incomplete surrogate pair, a partial <see cref="Separator"/>, or a character stripped of the combining
    /// marks that follow it. Because those units are kept whole, a slug can end up slightly shorter than the limit.
    /// </remarks>
    public int MaximumLength { get; set; }

    /// <summary>Gets or sets the separator string used between words. Default is "-".</summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public string Separator
    {
        get => field;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    }

    /// <summary>Gets or sets the culture to use for case transformations. When null, uses invariant culture.</summary>
    public CultureInfo? Culture { get; set; }

    /// <summary>Gets or sets a value indicating whether the generated slug can end with a separator.</summary>
    public bool CanEndWithSeparator { get; set; }

    /// <summary>Gets or sets the case transformation to apply to the slug.</summary>
    public CasingTransformation CasingTransformation { get; set; }

    /// <summary>Initializes a new instance of the <see cref="SlugOptions"/> class with default settings.</summary>
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

    /// <summary>Determines whether the specified character is allowed in the slug.</summary>
    /// <param name="character">The character to check.</param>
    /// <returns><see langword="true"/> if the character is allowed; otherwise, <see langword="false"/>.</returns>
    public virtual bool IsAllowed(Rune character)
    {
        var ranges = AllowedRanges;
        if (ranges.Count == 0)
            return true;

        // Avoid the closure allocated by LINQ: this runs for every rune of the input.
        for (var i = 0; i < ranges.Count; i++)
        {
            if (IsInRange(ranges[i], character))
                return true;
        }

        return false;
    }

    private static bool IsInRange(UnicodeRange range, Rune rune)
    {
        return rune.Value >= range.FirstCodePoint && rune.Value < (range.FirstCodePoint + range.Length);
    }

    /// <summary>Replaces a rune with its transformed version based on the configured casing transformation.</summary>
    /// <param name="rune">The rune to replace.</param>
    /// <returns>The transformed string representation of the rune.</returns>
    public virtual string Replace(Rune rune)
    {
        return Transform(rune).ToString();
    }

    /// <summary>
    /// Applies <see cref="CasingTransformation"/> to a rune without allocating the string <see cref="Replace(Rune)"/> returns.
    /// Only used when <see cref="Replace(Rune)"/> is known not to be overridden.
    /// </summary>
    internal Rune Transform(Rune rune)
    {
        return CasingTransformation switch
        {
            CasingTransformation.ToLowerCase => Culture is null ? Rune.ToLowerInvariant(rune) : Rune.ToLower(rune, Culture),
            CasingTransformation.ToUpperCase => Culture is null ? Rune.ToUpperInvariant(rune) : Rune.ToUpper(rune, Culture),
            _ => rune,
        };
    }

    /// <summary>
    /// Gets a value indicating whether <see cref="Replace(Rune)"/> still has its default implementation, in which case
    /// <see cref="Transform(Rune)"/> produces the same result without allocating a string for every rune.
    /// </summary>
    internal bool UsesDefaultReplace
    {
        get
        {
            var type = GetType();
            if (type == typeof(SlugOptions))
                return true;

            return UsesDefaultReplaceByType.GetOrAdd(type, _ =>
            {
                // Binding the delegate resolves the virtual call, so its target is the override that would run.
                // Reading the method off a delegate keeps this trimmer-friendly, unlike looking the override up
                // by name on a type that is only known at run time.
                Func<Rune, string> replace = Replace;
                return replace.Method.DeclaringType == typeof(SlugOptions);
            });
        }
    }
}
