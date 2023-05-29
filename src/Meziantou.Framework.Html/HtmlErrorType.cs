namespace Meziantou.Framework.Html;

#if HTML_PUBLIC
public
#else
internal
#endif
enum HtmlErrorType
{
    TagNotClosed,
    TagNotOpened,
    EncodingError,
    EncodingMismatch,
    NamespaceNotDeclared,
    DuplicateAttribute,
}
