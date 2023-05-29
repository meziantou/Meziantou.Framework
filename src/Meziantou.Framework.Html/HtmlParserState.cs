namespace Meziantou.Framework.Html;

// NOTE: keep in sync with HtmlFragmentType
#if HTML_PUBLIC
public
#else
internal
#endif
enum HtmlParserState
{
    Text,
    TagOpen,     // <
    TagEnd,      // -> TagEnd
    TagEndClose, // />
    TagClose,    // </body
    AttName,
    AttValue,
    CommentClose,
    CDataText,

    CommentOpen,
    TagStart,    // <body
    AttAssign,
    Atts,
    RawText,     // SCRIPT and STYLE special handling
    CData,
}
