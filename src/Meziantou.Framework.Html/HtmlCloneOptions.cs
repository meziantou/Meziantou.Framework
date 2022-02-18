#nullable disable

namespace Meziantou.Framework.Html;

[Flags]
public enum HtmlCloneOptions
{
    None = 0x0,
    Deep = 0x1,
    OverwriteAttributes = 0x2,
    Tag = 0x4,
    Attributes = 0x8,
    StreamOrder = 0x10,

    All = Deep | OverwriteAttributes | Tag | Attributes | StreamOrder,
}
