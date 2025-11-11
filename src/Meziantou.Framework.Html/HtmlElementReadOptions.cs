#nullable disable

namespace Meziantou.Framework.Html;

/// <summary>
/// Specifies options that control how HTML elements are read and parsed.
/// </summary>
[Flags]
#if HTML_PUBLIC
public
#else
internal
#endif
enum HtmlElementReadOptions
{
    /// <summary>No special read options.</summary>
    None = 0x0,

    /// <summary>The element's inner content should be read as raw text without parsing (e.g., script, style tags).</summary>
    InnerRaw = 0x1,

    /// <summary>The element is automatically closed and doesn't require an explicit closing tag (e.g., br, img, input).</summary>
    AutoClosed = 0x2,

    /// <summary>The element cannot have child elements (e.g., void elements).</summary>
    NoChild = 0x4,
}
