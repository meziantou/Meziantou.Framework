#nullable disable

namespace Meziantou.Framework.Html;

[Flags]
public enum HtmlNodeNavigatorOptions
{
    None = 0,
    UppercasedNames = 0x1,
    LowercasedNames = 0x2,
    UppercasedPrefixes = 0x4,
    LowercasedPrefixes = 0x8,
    UppercasedNamespaceURIs = 0x10,
    LowercasedNamespaceURIs = 0x20,
    UppercasedValues = 0x40,
    LowercasedValues = 0x80,
    Dynamic = 0x100,
    RootNode = 0x200,
    DepthFirst = 0x400,

    UppercasedAll = UppercasedNames | UppercasedPrefixes | UppercasedNamespaceURIs | UppercasedValues,
    LowercasedAll = LowercasedNames | LowercasedPrefixes | LowercasedNamespaceURIs | LowercasedValues,
}
