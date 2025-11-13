#nullable disable

namespace Meziantou.Framework.Html;

/// <summary>Specifies options for cloning HTML nodes.</summary>
[Flags]
#if HTML_PUBLIC
public
#else
internal
#endif
enum HtmlCloneOptions
{
    /// <summary>No options specified.</summary>
    None = 0x0,

    /// <summary>Clone child nodes recursively.</summary>
    Deep = 0x1,

    /// <summary>Overwrite existing attributes in the target node.</summary>
    OverwriteAttributes = 0x2,

    /// <summary>Clone the Tag property.</summary>
    Tag = 0x4,

    /// <summary>Clone attributes.</summary>
    Attributes = 0x8,

    /// <summary>Clone the StreamOrder property.</summary>
    StreamOrder = 0x10,

    /// <summary>Clone everything: deep children, attributes (overwriting), tag, and stream order.</summary>
    All = Deep | OverwriteAttributes | Tag | Attributes | StreamOrder,
}
