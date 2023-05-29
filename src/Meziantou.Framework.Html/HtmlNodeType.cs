namespace Meziantou.Framework.Html;

#if HTML_PUBLIC
public
#else
internal
#endif
enum HtmlNodeType
{
    Attribute,
    Comment,
    Document,
    Element,
    EndElement,
    Text,
    None,
    ProcessingInstruction,
    DocumentType,
    XPathResult,
}
