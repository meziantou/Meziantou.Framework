using System.Collections.ObjectModel;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Typography;

/// <summary>
/// The fonts of a text, by order of preference: a primary font, then the fonts that are used for the characters the
/// ones before them do not have.
/// </summary>
/// <remarks>
/// <para>
/// Each character, with the marks and the selectors that follow it, is drawn with the first font of the stack that has
/// all of it; a character no font has is drawn with the missing glyph of the primary font. The metrics of a line come
/// from the fonts that are used on it.
/// </para>
/// <para>
/// The fonts installed on the machine are never used by themselves: a stack only has the fonts it is given, and draws
/// the same text the same way on every machine. <see cref="WithInstalledFallback(SystemFontCollection?, InstalledFontFallbackOptions?)"/>
/// gives a stack that also looks for the characters its fonts do not have among the installed fonts.
/// </para>
/// <para>A stack is immutable and can be used by several threads at the same time. A <see cref="Font"/> converts implicitly to a stack of one font.</para>
/// </remarks>
public sealed class FontStack
{
    private readonly Font[] _fonts;

    /// <summary>Initializes a stack of one font.</summary>
    /// <param name="primary">The font.</param>
    /// <exception cref="ArgumentNullException"><paramref name="primary"/> is <see langword="null"/>.</exception>
    public FontStack(Font primary)
    {
        ArgumentNullException.ThrowIfNull(primary);
        _fonts = [primary];
        Fonts = new ReadOnlyCollection<Font>(_fonts);
    }

    private FontStack(Font[] fonts, InstalledFontFallback fallback)
    {
        _fonts = fonts;
        Fonts = new ReadOnlyCollection<Font>(_fonts);
        InstalledFallback = fallback;
    }

    /// <summary>Initializes a stack of fonts.</summary>
    /// <param name="primary">The font used first, whose missing glyph is drawn for a character no font has.</param>
    /// <param name="fallbacks">The fonts used, in order, for the characters the fonts before them do not have.</param>
    /// <exception cref="ArgumentNullException"><paramref name="primary"/> or one of <paramref name="fallbacks"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">There are more than 64 fonts.</exception>
    public FontStack(Font primary, params ReadOnlySpan<Font> fallbacks)
    {
        ArgumentNullException.ThrowIfNull(primary);
        if (fallbacks.Length > MaxFontCount - 1)
            throw new ArgumentException("A font stack has at most 64 fonts.", nameof(fallbacks));

        _fonts = new Font[fallbacks.Length + 1];
        _fonts[0] = primary;
        for (var i = 0; i < fallbacks.Length; i++)
        {
            _fonts[i + 1] = fallbacks[i] ?? throw new ArgumentNullException(nameof(fallbacks), "A fallback font is null.");
        }

        Fonts = new ReadOnlyCollection<Font>(_fonts);
    }

    /// <summary>The largest number of fonts of a stack.</summary>
    internal const int MaxFontCount = 64;

    /// <summary>Gets the font used first.</summary>
    public Font Primary => _fonts[0];

    /// <summary>Gets the fonts, by order of preference; the first one is <see cref="Primary"/>.</summary>
    public IReadOnlyList<Font> Fonts { get; }

    /// <summary>Gets a value indicating whether the characters the fonts of the stack do not have are looked for among installed fonts.</summary>
    /// <seealso cref="WithInstalledFallback(SystemFontCollection?, InstalledFontFallbackOptions?)"/>
    public bool HasInstalledFallback => InstalledFallback is not null;

    internal ReadOnlySpan<Font> FontSpan => _fonts;

    internal InstalledFontFallback? InstalledFallback { get; }

    /// <summary>
    /// Creates a stack of the same fonts that, for a character none of them has, looks for a font among the installed
    /// fonts.
    /// </summary>
    /// <param name="fonts">The fonts to look in, or <see langword="null"/> for <see cref="SystemFontCollection.Default"/>, the fonts installed on the machine.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="InstalledFontFallbackOptions.Default"/>.</param>
    /// <returns>A stack with the fonts of this one, then the installed fonts.</returns>
    /// <remarks>
    /// <para>
    /// The fonts of the stack are used first, as without fallback. A character, with the marks and the selectors that
    /// follow it, that none of them has is looked for among the installed fonts, in this order: the families of
    /// <see cref="InstalledFontFallbackOptions.PreferredFamilies"/>; for an emoji presented with colors (Unicode
    /// Technical Standard #51), the color emoji fonts (Segoe UI Emoji, Apple Color Emoji, Noto Color Emoji); the
    /// families the platforms usually use for the script of the character (and, for the Han ideographs, the language of
    /// the text), and the fonts of symbols; then every family that has the character, those
    /// whose closest face has the weight and the italic of the primary font first, by family name and by file. In a
    /// family, the face is the one closest to the style of the primary font, at the instance of that style when it is
    /// a variable font (<see cref="Font.LoadInstalled"/>). A character no font has is drawn with the missing glyph of the
    /// primary font.
    /// </para>
    /// <para>
    /// The Han ideographs have different forms in Chinese, Japanese and Korean: their families are chosen by the
    /// language of the text (<see cref="TextStyle.Language"/>): Microsoft YaHei, PingFang SC or Noto Sans CJK SC for
    /// Simplified Chinese and a text without a language, Microsoft JhengHei, PingFang TC or Noto Sans CJK TC for
    /// Traditional Chinese, PingFang HK or Noto Sans CJK HK for Hong Kong, Yu Gothic, Hiragino Sans or Noto Sans CJK JP
    /// for Japanese, Malgun Gothic, Apple SD Gothic Neo or Noto Sans CJK KR for Korean, then those of Simplified Chinese.
    /// The fonts found are kept for each language. <see cref="InstalledFontFallbackOptions.PreferredFamilies"/> still
    /// come first, whatever the language.
    /// </para>
    /// <para>
    /// The installed fonts differ from one machine to another, and so does a text drawn with this stack: use it when
    /// the text matters more than the exact pixels. The characters of a face are read from its file the first time the
    /// fallback needs them (<see cref="SystemFontDescriptor.Covers(Rune)"/>); a file that cannot be loaded is skipped. The
    /// font found for a character is kept by the stack, which its layouts share: each font is loaded once, the same
    /// character always gets the same font, and the fonts it loaded live as long as the stack.
    /// </para>
    /// </remarks>
    public FontStack WithInstalledFallback(SystemFontCollection? fonts = null, InstalledFontFallbackOptions? options = null)
    {
        return new FontStack(_fonts, new InstalledFontFallback(fonts ?? SystemFontCollection.Default, options ?? InstalledFontFallbackOptions.Default, Primary));
    }

    /// <summary>Creates a stack of one font.</summary>
    /// <param name="font">The font.</param>
    /// <exception cref="ArgumentNullException"><paramref name="font"/> is <see langword="null"/>.</exception>
    public static implicit operator FontStack(Font font) => new(font);

    /// <summary>Creates a stack of one font.</summary>
    /// <param name="font">The font.</param>
    /// <returns>A stack whose primary font is <paramref name="font"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="font"/> is <see langword="null"/>.</exception>
    public static FontStack FromFont(Font font) => new(font);
}
