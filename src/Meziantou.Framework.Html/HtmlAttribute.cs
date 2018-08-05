using System;
using System.IO;
using System.Xml;

namespace Meziantou.Framework.Html
{
    public class HtmlAttribute : HtmlNode
    {
        private char _quoteChar;
        private char _nameQuoteChar;
        private bool _isValueDefined;
        private bool _escapeQuoteChar;

        protected internal HtmlAttribute(string prefix, string localName, string namespaceURI, HtmlDocument ownerDocument)
            : base(prefix, localName, namespaceURI, ownerDocument)
        {
            _escapeQuoteChar = true;
        }

        public override string NamespaceURI
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Prefix))
                    return string.Empty;

                return base.NamespaceURI;
            }
            set => base.NamespaceURI = value;
        }

        public virtual bool IsNamespace => NamespaceURI?.Equals(XmlnsNamespaceURI) == true;

        public virtual bool EscapeQuoteChar
        {
            get => _escapeQuoteChar;
            set
            {
                if (value != _escapeQuoteChar)
                {
                    _escapeQuoteChar = value;
                    OnPropertyChanged();
                }
            }
        }

        public virtual bool IsValueDefined
        {
            get => _isValueDefined;
            set
            {
                if (value != _isValueDefined)
                {
                    _isValueDefined = value;
                    OnPropertyChanged();
                }
            }
        }

        public virtual char QuoteChar
        {
            get => _quoteChar;
            set
            {
                if (value != _quoteChar)
                {
                    _quoteChar = value;
                    OnPropertyChanged();
                }
            }
        }

        public virtual char NameQuoteChar
        {
            get => _nameQuoteChar;
            set
            {
                if (value != _nameQuoteChar)
                {
                    _nameQuoteChar = value;
                    OnPropertyChanged();
                }
            }
        }

        public HtmlElement OwnerElement => (HtmlElement)ParentNode;

        public override HtmlNodeType NodeType => HtmlNodeType.Attribute;

        public override int ParentIndex
        {
            get
            {
                if (ParentNode?.HasAttributes == true)
                {
                    for (var i = 0; i < ParentNode.Attributes.Count; i++)
                    {
                        if (ParentNode.Attributes[i] == this)
                            return i;
                    }
                }

                return -1;
            }
        }

        public new HtmlAttribute NextSibling
        {
            get
            {
                if (ParentNode == null || !ParentNode.HasAttributes)
                    return null;

                var index = ParentIndex;
                if (index < 0 || (index + 1) >= ParentNode.Attributes.Count)
                    return null;

                return ParentNode.Attributes[index + 1];
            }
        }

        public new HtmlAttribute PreviousSibling
        {
            get
            {
                if (ParentNode == null || !ParentNode.HasAttributes)
                    return null;

                var index = ParentIndex;
                if (index <= 0)
                    return null;

                return ParentNode.Attributes[index - 1];
            }
        }

        public override string Value
        {
            get => InnerText;
            set
            {
                IsValueDefined = value != null;
                InnerText = value;
            }
        }

        public override void WriteTo(TextWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            if (NameQuoteChar != '\0')
            {
                writer.Write(NameQuoteChar);
            }

            if (!string.IsNullOrEmpty(Prefix))
            {
                writer.Write(Prefix);
                writer.Write(':');
                writer.Write(LocalName);
            }
            else
            {
                writer.Write(Name);
            }

            if (NameQuoteChar != '\0')
            {
                writer.Write(NameQuoteChar);
            }

            if (Value != null && IsValueDefined)
            {
                writer.Write('=');

                var quoteChar = GetQuoteChar();
                if (quoteChar == '\0')
                {
                    WriteContentToWhenUndefinedQuoteChar(writer);
                    return;
                }

                if (OwnerDocument?.IsXhtml == true)
                {
                    if (quoteChar != '\'' && quoteChar != '"')
                    {
                        quoteChar = '\"';
                    }
                }

                if (quoteChar != '\0')
                {
                    writer.Write(quoteChar);
                }

                WriteContentTo(writer);

                if (quoteChar != '\0')
                {
                    writer.Write(quoteChar);
                }
            }
        }

        protected virtual char GetQuoteChar()
        {
            return QuoteChar;
        }

        public virtual void WriteContentToWhenUndefinedQuoteChar(TextWriter writer)
        {
            var eqc = EscapeQuoteChar;
            var s = GetValue(ref eqc);
            if (string.IsNullOrWhiteSpace(s) || s.IndexOf('"') < 0)
            {
                writer.Write('"');
                writer.Write(s);
                writer.Write('"');
                return;
            }

            writer.Write('\'');
            writer.Write(eqc ? s.Replace("'", "&apos;") : s);
            writer.Write('\'');
        }

        internal static string UnescapeText(string text, char quoteChar)
        {
            if (text == null)
                return null;

            if (quoteChar == '"')
                return text.Replace("&quot;", quoteChar.ToString());

            return text.Replace("&apos;", quoteChar.ToString());
        }

        protected virtual string GetValue(ref bool escapeQuoteChar)
        {
            using (var sw = new StringWriter())
            {
                foreach (var node in ChildNodes)
                {
                    node.WriteTo(sw);
                }
                return sw.ToString();
            }
        }

        public override void WriteContentTo(TextWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            var eqc = EscapeQuoteChar;
            var s = GetValue(ref eqc);
            if (s != null)
            {
                if (eqc)
                {
                    if (QuoteChar == '"')
                    {
                        s = s.Replace(QuoteChar.ToString(), "&quot;");
                    }
                    else if (QuoteChar == '\'')
                    {
                        s = s.Replace(QuoteChar.ToString(), "&apos;");
                    }
                }
                writer.Write(s);
            }
        }

        public override void WriteTo(XmlWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            if (Prefix == XmlnsPrefix || Name == XmlnsPrefix)
                return;

            writer.WriteStartAttribute(GetValidXmlName(Prefix), GetValidXmlName(LocalName), NamespaceURI);
            WriteContentTo(writer);
            writer.WriteEndAttribute();
        }

        public override void WriteContentTo(XmlWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            foreach (var node in ChildNodes)
            {
                node.WriteTo(writer);
            }
        }

        public override void CopyTo(HtmlNode target, HtmlCloneOptions options)
        {
            base.CopyTo(target, HtmlCloneOptions.None); // don't do deep copy
            var att = (HtmlAttribute)target;
            att._nameQuoteChar = _nameQuoteChar;
            att._quoteChar = _quoteChar;
            att._isValueDefined = _isValueDefined;
            att._escapeQuoteChar = _escapeQuoteChar;
        }
    }
}
