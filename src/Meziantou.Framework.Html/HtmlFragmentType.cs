namespace Meziantou.Framework.Html;

// NOTE: keep in sync with HtmlParserState
#if HTML_PUBLIC
public
#else
internal
#endif
enum HtmlFragmentType
{
    Text,
    TagOpen,     // <
    TagEnd,      // -> TagEnd
    TagEndClose, // />
    TagClose,    // </body
    AttName,
    AttValue,
    Comment,
    CDataText,
}
