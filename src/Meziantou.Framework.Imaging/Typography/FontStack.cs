using System.Collections.ObjectModel;

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
/// <para>A stack is immutable. A <see cref="Font"/> converts implicitly to a stack of one font.</para>
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

    internal ReadOnlySpan<Font> FontSpan => _fonts;

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
