#nullable disable
using System.ComponentModel;

namespace Meziantou.Framework.Html;

#if HTML_PUBLIC
public
#else
internal
#endif
sealed class HtmlDocumentParseEventArgs : CancelEventArgs
{
    public HtmlDocumentParseEventArgs(HtmlReader reader)
    {
        Reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public HtmlReader Reader { get; }
    public Encoding DetectedEncoding { get; set; }
    public HtmlNode CurrentNode { get; set; }
    public HtmlAttribute CurrentAttribute { get; set; }
    public bool Continue { get; set; }
}
