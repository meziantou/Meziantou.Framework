#nullable disable

namespace Meziantou.Framework.Html;

[Flags]
public enum HtmlElementWriteOptions
{
    None = 0x0,
    DontCloseIfEmpty = 0x1,
    AlwaysClose = 0x2,
    NoChild = 0x4,
}
