namespace Meziantou.Framework.WebPlatform.Unicode;

/// <summary>The kind of line break opportunity before a character (see <see cref="LineBreaker"/>).</summary>
internal enum LineBreakOpportunity : byte
{
    /// <summary>No line break is allowed.</summary>
    None,

    /// <summary>
    /// A grapheme cluster boundary where <c>overflow-wrap: anywhere</c> or <c>break-word</c> allows a break when there is no
    /// other soft wrap opportunity in the line (https://drafts.csswg.org/css-text-4/#overflow-wrap-property).
    /// </summary>
    Emergency,

    /// <summary>
    /// A soft wrap opportunity inside a phrase of <c>word-break: auto-phrase</c>
    /// (https://drafts.csswg.org/css-text-4/#valdef-word-break-auto-phrase): "prioritize keeping natural phrases (of multiple
    /// words) together", so the line only breaks there when no other soft wrap opportunity fits (as in Chrome, which breaks the
    /// line again without the phrases when it overflows). It does not contribute to the min-content size.
    /// </summary>
    Phrase,

    /// <summary>A soft wrap opportunity (https://drafts.csswg.org/css-text-4/#soft-wrap-opportunity).</summary>
    Allowed,

    /// <summary>
    /// A soft wrap opportunity after a soft hyphen (U+00AD): when the line breaks there, a hyphen (<c>hyphenate-character</c>)
    /// is displayed at the end of the line (https://drafts.csswg.org/css-text-4/#hyphenation).
    /// </summary>
    Hyphen,

    /// <summary>
    /// An automatic hyphenation opportunity of <c>hyphens: auto</c>, found by the hyphenation dictionary of the language of the
    /// text (<see cref="Hyphenator"/>): it is added by the inline layout, not by <see cref="LineBreaker"/>. As after a soft hyphen,
    /// a hyphen is displayed at the end of the line when it breaks there (https://drafts.csswg.org/css-text-4/#hyphenation).
    /// </summary>
    AutoHyphen,

    /// <summary>A forced line break, after a hard line break character (UAX #14 rules LB4 and LB5).</summary>
    Mandatory,
}

/// <summary>The <c>line-break</c> property (https://drafts.csswg.org/css-text-4/#line-break-property); <c>auto</c> is <see cref="Normal"/>.</summary>
internal enum LineBreakStrictness : byte
{
    Normal,
    Loose,
    Strict,
    Anywhere,
}

/// <summary>
/// The <c>word-break</c> property (https://drafts.csswg.org/css-text-4/#word-break-property). The deprecated <c>break-word</c>
/// keyword is <see cref="Normal"/> with <c>overflow-wrap: anywhere</c>.
/// </summary>
internal enum WordBreakMode : byte
{
    Normal,
    BreakAll,
    KeepAll,
    Manual,

    /// <summary>
    /// <see cref="Normal"/>, with the phrases of Japanese kept together (<see cref="LineBreakOpportunity.Phrase"/>, when the content
    /// language is Japanese, see <see cref="JapanesePhraseBreaker"/>), and the hyphenation opportunities only used when no other
    /// opportunity fits.
    /// </summary>
    AutoPhrase,
}

/// <summary>The <c>overflow-wrap</c> property (https://drafts.csswg.org/css-text-4/#overflow-wrap-property).</summary>
internal enum OverflowWrapMode : byte
{
    Normal,
    Anywhere,
    BreakWord,
}

/// <summary>
/// The <c>hyphens</c> property (https://drafts.csswg.org/css-text-4/#hyphens-property). For <see cref="LineBreaker"/>,
/// <see cref="Auto"/> is <see cref="Manual"/> (the soft hyphens): the inline layout adds the automatic hyphenation opportunities
/// (<see cref="LineBreakOpportunity.AutoHyphen"/>).
/// </summary>
internal enum HyphensMode : byte
{
    Manual,
    None,
    Auto,
}

/// <summary>
/// The CSS properties that tailor the line breaking rules of UAX #14 (https://drafts.csswg.org/css-text-4/#line-breaking).
/// The default value is the initial value of the properties.
/// </summary>
internal readonly record struct LineBreakOptions
{
    /// <summary>The default rules of UAX #14, without the CSS tailorings (used by the Unicode conformance tests).</summary>
    public static LineBreakOptions UnicodeDefault { get; } = new() { IsUnicodeDefault = true };

    public LineBreakStrictness Strictness { get; init; }

    public WordBreakMode WordBreak { get; init; }

    public OverflowWrapMode OverflowWrap { get; init; }

    public HyphensMode Hyphens { get; init; }

    /// <summary>
    /// <c>white-space-collapse: break-spaces</c>: there is a soft wrap opportunity after every preserved white space
    /// character, including between white space characters (https://drafts.csswg.org/css-text-4/#valdef-white-space-collapse-break-spaces).
    /// </summary>
    public bool BreakSpaces { get; init; }

    /// <summary>Whether the content language is Chinese or Japanese, which enables some rules of <c>line-break</c>.</summary>
    public bool IsChineseOrJapanese { get; init; }

    /// <summary>Whether the content language is Japanese, whose phrases are detected by <c>word-break: auto-phrase</c>.</summary>
    public bool IsJapanese { get; init; }

    internal bool IsUnicodeDefault { get; init; }
}

/// <summary>The options of the text from <see cref="Start"/> (a UTF-16 index) to the start of the next run.</summary>
internal readonly record struct LineBreakOptionsRun(int Start, LineBreakOptions Options);
