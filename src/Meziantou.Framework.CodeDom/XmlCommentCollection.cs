using System.Xml.Linq;

namespace Meziantou.Framework.CodeDom
{
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
            Add(new XmlComment(new XElement("summary", description)));
        }

        public void AddReturn(string text)
        {
            Add(new XmlComment(new XElement("return", text)));
        }

        public void AddParam(string name, string description)
        {
            Add(new XmlComment(new XElement("param", new XAttribute("name", name), description)));
        }

        public void AddTypeParam(string name, string description)
        {
            Add(new XmlComment(new XElement("typeparam", new XAttribute("name", name), description)));
        }
    }
}
