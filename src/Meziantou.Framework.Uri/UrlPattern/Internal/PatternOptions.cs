namespace Meziantou.Framework.UrlPatternInternal;

// https://urlpattern.spec.whatwg.org/#options-header

/// <summary>Options struct contains different settings that control how pattern strings behave.</summary>
/// <remarks>
/// <see href="https://urlpattern.spec.whatwg.org/#options-header">WHATWG URL Pattern Spec - Options</see>
/// </remarks>
internal sealed class PatternOptions
{
    /// <summary>Gets or sets the delimiter code point. This code point is treated as a segment separator.</summary>
    /// <remarks>For example, if the delimiter code point is "/" then "/:foo" will match "/bar", but not "/bar/baz".</remarks>
    public string DelimiterCodePoint { get; init; } = "";

    /// <summary>Gets or sets the prefix code point. The code point is treated as an automatic prefix if found immediately preceding a match group.</summary>
    /// <remarks>For example, if prefix code point is "/" then "/foo/:bar?/baz" will treat the "/" before ":bar" as a prefix that becomes optional along with the named group.</remarks>
    public string PrefixCodePoint { get; init; } = "";

    /// <summary>Gets or sets whether to ignore case when matching.</summary>
    public bool IgnoreCase { get; init; }

    /// <summary>Default options with empty delimiter and prefix.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#default-options">WHATWG URL Pattern Spec - Default options</see>
    /// </remarks>
    public static PatternOptions Default { get; } = new PatternOptions();

    /// <summary>Hostname options with "." as delimiter.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#hostname-options">WHATWG URL Pattern Spec - Hostname options</see>
    /// </remarks>
    public static PatternOptions Hostname { get; } = new PatternOptions { DelimiterCodePoint = "." };

    /// <summary>Pathname options with "/" as delimiter and prefix.</summary>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#pathname-options">WHATWG URL Pattern Spec - Pathname options</see>
    /// </remarks>
    public static PatternOptions Pathname { get; } = new PatternOptions { DelimiterCodePoint = "/", PrefixCodePoint = "/" };

    /// <summary>Creates a copy of this options with the specified ignore case setting.</summary>
    public PatternOptions WithIgnoreCase(bool ignoreCase)
    {
        return new PatternOptions
        {
            DelimiterCodePoint = DelimiterCodePoint,
            PrefixCodePoint = PrefixCodePoint,
            IgnoreCase = ignoreCase,
        };
    }
}
