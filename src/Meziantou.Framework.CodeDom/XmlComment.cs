using System.Xml.Linq;

namespace Meziantou.Framework.CodeDom;

public class XmlComment : CodeObject
{
    public XmlComment()
    {
    }

    public XmlComment(XElement? element)
    {
        Element = element;
    }

    public XElement? Element { get; set; }
}
