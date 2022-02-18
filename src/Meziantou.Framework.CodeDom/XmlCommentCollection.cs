using System.Xml.Linq;

namespace Meziantou.Framework.CodeDom;

public class XmlCommentCollection : CodeObjectCollection<XmlComment>
{
    public XmlCommentCollection(CodeObject parent) : base(parent)
    {
    }

    public void Add(XElement element)
    {
        Add(new XmlComment(element));
    }

    public void AddSummary(string description)
    {
        Add(new XmlComment(new XElement(XName.Get("summary"), description)));
    }

    public void AddReturn(string text)
    {
        Add(new XmlComment(new XElement(XName.Get("return"), text)));
    }

    public void AddParam(string name, string description)
    {
        Add(new XmlComment(new XElement(XName.Get("param"), new XAttribute(XName.Get("name"), name), description)));
    }

    public void AddTypeParam(string name, string description)
    {
        Add(new XmlComment(new XElement(XName.Get("typeparam"), new XAttribute(XName.Get("name"), name), description)));
    }
}
