#nullable disable
using System.Xml;

namespace Meziantou.Framework.Html;

// this is used only when a XPATH query does not return a node-set, for example "count(//td)" returns a number
#if HTML_PUBLIC
public
#else
internal
#endif
sealed class HtmlXPathResult : HtmlNode
{
    internal HtmlXPathResult(HtmlDocument ownerDocument, object result)
        : base(string.Empty, "#result", string.Empty, ownerDocument)
    {
        Result = result;
    }

    public object Result { get; private set; }

    public override string Value
    {
        get
        {
            if (Result is null)
                return null;

            return string.Format(CultureInfo.InvariantCulture, "{0}", Result);
        }
        set => Result = value;
    }

    public override HtmlNodeType NodeType => HtmlNodeType.XPathResult;

    public override void WriteTo(TextWriter writer)
    {
        if (writer is not null && Result is not null)
        {
            writer.Write(Result);
        }
    }

    public override void WriteContentTo(TextWriter writer)
    {
        if (writer is not null && Result is not null)
        {
            writer.Write(Result);
        }
    }

    public override void WriteTo(XmlWriter writer)
    {
        if (writer is not null && Result is not null)
        {
            writer.WriteValue(Result);
        }
    }

    public override void WriteContentTo(XmlWriter writer)
    {
        if (writer is not null && Result is not null)
        {
            writer.WriteValue(Result);
        }
    }
}
