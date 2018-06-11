using System;
using System.Globalization;
using System.Xml;

namespace Meziantou.Framework.Html
{
    public class HtmlXmlWriter : XmlWriter
    {
        private WriteState _writeState;

        public HtmlXmlWriter(HtmlNode parent)
        {
            Parent = parent ?? new HtmlDocument();

            if (Parent.OwnerDocument == null)
                throw new ArgumentException(null, nameof(parent));

            Current = Parent;
            _writeState = WriteState.Start;
        }

        public HtmlDocument OwnerDocument
        {
            get
            {
                return Parent.OwnerDocument;
            }
        }

        public HtmlNode Parent { get; }
        public HtmlNode Current { get; private set; }

        public override WriteState WriteState
        {
            get
            {
                return _writeState;
            }
        }

        public override void Flush()
        {
        }

        public override string LookupPrefix(string ns)
        {
            return Parent.OwnerDocument.GetPrefixOfNamespace(ns);
        }

        public override void WriteBase64(byte[] buffer, int index, int count)
        {
            throw new NotSupportedException();
        }

        public override void WriteCData(string text)
        {
            if (text == null)
                return;

            if (Current is HtmlAttribute att)
            {
                att.Value = text;
                return;
            }

            HtmlText node = Parent.OwnerDocument.CreateText();
            node.Value = text;
            Current.AppendChild(node);
        }

        public override void WriteCharEntity(char ch)
        {
            WriteCData("&#x" + ((int)ch).ToString("X", NumberFormatInfo.InvariantInfo) + ";");
        }

        public override void WriteChars(char[] buffer, int index, int count)
        {
            WriteRaw(buffer, index, count);
        }

        public override void WriteComment(string text)
        {
            if (text == null)
                return;

            HtmlComment node = Parent.OwnerDocument.CreateComment();
            node.Value = text;
            Current.AppendChild(node);
        }

        public override void WriteDocType(string name, string pubid, string sysid, string subset)
        {
            string text = "<!DOCTYPE " +  name;
            if (pubid != null)
            {
                text += " PUBLIC \"" + pubid + "\" \"" + sysid + "\"";
            }
            else if (sysid != null)
            {
                text += " SYSTEM \"" + sysid + "\"";
            }
            if (subset != null)
            {
                text += "[" + subset + "]";
            }
            text += ">";
            WriteCData(text);
        }

        private HtmlElement GetCurrentElement()
        {
            var element = Current as HtmlElement;
            if (element == null)
                throw new InvalidOperationException($"Current node is not an element but is of '{Current.GetType().FullName}' type.");

            return element;
        }

        public override void WriteEndElement()
        {
            Current = GetCurrentElement().ParentNode;
            _writeState = WriteState.Element;
        }

        public override void WriteEntityRef(string name)
        {
            WriteCData("&" + name + ";");
        }

        public override void WriteFullEndElement()
        {
            WriteEndElement();
        }

        public override void WriteProcessingInstruction(string name, string text)
        {
            WriteCData("<?" + name + " " + text + "?>");
        }

        public override void WriteRaw(string data)
        {
            WriteCData(data);
        }

        public override void WriteRaw(char[] buffer, int index, int count)
        {
            throw new NotSupportedException();
        }

        public override void WriteStartAttribute(string prefix, string localName, string ns)
        {
            HtmlElement current = GetCurrentElement();
            Current = current.Attributes.Add(prefix, localName, ns);
            _writeState = WriteState.Attribute;
        }

        public override void WriteEndAttribute()
        {
            var att = Current as HtmlAttribute;
            if (att == null)
                throw new InvalidOperationException("Current node is not an attribute but is of '" + Current.GetType().FullName + "' type.");

            Current = att.ParentNode;
            _writeState = WriteState.Element;
        }

        public override void WriteStartDocument(bool standalone)
        {
            throw new NotSupportedException();
        }

        public override void WriteStartDocument()
        {
            WriteStartDocument(false);
        }

        public override void WriteEndDocument()
        {
            throw new NotSupportedException();
        }

        public override void WriteStartElement(string prefix, string localName, string ns)
        {
            HtmlElement element = OwnerDocument.CreateElement(prefix, localName, ns);
            Current.AppendChild(element);
            Current = element;
            _writeState = WriteState.Element;
        }

        public override void WriteString(string text)
        {
            WriteCData(text);
        }

        private static int CombineSurrogateChar(int lowChar, int highChar)
        {
            return (lowChar - 0xdc00) | (((highChar - 0xd800) << 10) + 0x10000);
        }

        public override void WriteSurrogateCharEntity(char lowChar, char highChar)
        {
            int c = CombineSurrogateChar(lowChar, highChar);
            WriteCData("&#x" + c.ToString("X", NumberFormatInfo.InvariantInfo) + ";");
        }

        public override void WriteWhitespace(string ws)
        {
            WriteCData(ws);
        }
    }
}
