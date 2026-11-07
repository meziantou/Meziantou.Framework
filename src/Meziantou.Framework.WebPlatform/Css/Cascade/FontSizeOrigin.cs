namespace Meziantou.Framework.WebPlatform.Css.Cascade;

/// <summary>
/// How the font size of an element was specified, inherited with the font size: the font size follows the default font size
/// of the generic font family (13px for <c>monospace</c> in the browsers, 16px otherwise) unless it is an absolute length
/// (see <see cref="ComputedStyle"/>).
/// </summary>
internal enum FontSizeOrigin : byte
{
    /// <summary>An absolute length (or a value that is not known, e.g. an animated value): the font size does not depend on the font family.</summary>
    AbsoluteLength,

    /// <summary>
    /// A size relative to the font size of the parent, which is not an absolute length: a percentage, a length relative to the
    /// font of the element (<c>em</c>, <c>ex</c>...), <c>larger</c>, <c>smaller</c>, <c>math</c>.
    /// </summary>
    Relative,

    XxSmall,
    XSmall,
    Small,
    Medium,
    Large,
    XLarge,
    XxLarge,
    XxxLarge,
}
