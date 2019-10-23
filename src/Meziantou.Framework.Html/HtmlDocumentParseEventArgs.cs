#nullable disable
using System;
using System.ComponentModel;
using System.Text;

namespace Meziantou.Framework.Html
{
    public sealed class HtmlDocumentParseEventArgs : CancelEventArgs
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
}
