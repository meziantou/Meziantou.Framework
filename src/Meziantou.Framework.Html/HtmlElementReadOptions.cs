#nullable disable

namespace Meziantou.Framework.Html;

[Flags]
#if HTML_PUBLIC
public
#else
internal
#endif
enum HtmlElementReadOptions
{
    None = 0x0,
    InnerRaw = 0x1,
    AutoClosed = 0x2,
    NoChild = 0x4,
}
