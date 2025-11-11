using System.Xml.Linq;

namespace Meziantou.Framework.CodeDom;

/// <summary>Represents an XML documentation comment element.</summary>
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
