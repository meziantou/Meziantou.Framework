using System;
using System.Diagnostics;
using System.IO;
using System.Xml;

namespace Meziantou.Framework.Html
{
    [DebuggerDisplay("{Name}")]
    public class HtmlElement : HtmlNode
    {
        private bool? _empty;
        private bool? _dontCloseIfEmpty;
        private bool? _alwaysClose;
        private bool? _noChild;
        private bool _processingInstruction;
        private bool _closed = true;
        private char _closeChar = '/';
        private HtmlNodeType _nodeType;

        protected internal HtmlElement(string prefix, string localName, string namespaceURI, HtmlDocument ownerDocument)
            : base(prefix, localName, namespaceURI, ownerDocument)
        {
            _nodeType = IsDocumentType ? HtmlNodeType.DocumentType : HtmlNodeType.Element;
#if DEBUG_HTML_ID
            SetAttribute(DebugIdAttributeName, _debugId.ToString());
            _debugId++;
#endif
        }

#if DEBUG_HTML_ID
        public const string DebugIdAttributeName = "__id";
        private static int _debugId;

        public int DebugId
        {
            get
            {
                return GetAttributeValue(DebugIdAttributeName, -1);
            }
        }
#endif

        public virtual bool IsDocumentType
        {
            get
            {
                return Name.EqualsIgnoreCase("!doctype");
            }
        }

        public virtual char CloseChar
        {
            get
            {
                return _closeChar;
            }
            set
            {
                if (value != _closeChar)
                {
                    _closeChar = value;
                    OnPropertyChanged();
                }
            }
        }

        public virtual bool IsProcessingInstruction
        {
            get
            {
                return _processingInstruction;
            }
            set
            {
                if (value != _processingInstruction)
                {
                    if (value)
                    {
                        _nodeType = HtmlNodeType.ProcessingInstruction;
                    }
                    else if (IsDocumentType)
                    {
                        _nodeType = HtmlNodeType.DocumentType;
                    }
                    else
                    {
                        _nodeType = HtmlNodeType.Element;
                    }
                    ClearCaches();
                    _processingInstruction = value;
                    OnPropertyChanged();
                }
            }
        }

        public override string InnerHtml
        {
            get
            {
                return base.InnerHtml;
            }
            set
            {
                if (value != base.InnerHtml)
                {
                    RemoveAll();
                    if (value != null)
                    {
                        HtmlDocument doc = OwnerDocument != null ? OwnerDocument.CreateDocument() : new HtmlDocument();
                        doc.LoadHtml(value);
                        if (doc.HasChildNodes)
                        {
                            foreach (HtmlNode node in doc.ChildNodes)
                            {
                                ChildNodes.AddNoCheck(node);
                            }
                        }
                    }
                    ClearCaches();
                    OnPropertyChanged();
                }
            }
        }

        public virtual bool IsClosed
        {
            get
            {
                return _closed;
            }
            set
            {
                if (value != _closed)
                {
                    _closed = value;
                    ClearCaches();
                }
            }
        }

        public virtual bool IsEmpty
        {
            get
            {
                if (NoChild)
                    return true;

                if (HasChildNodes)
                    return false;

                if (IsProcessingInstruction)
                    return true;

                if (_empty.HasValue)
                    return _empty.Value;

                return true;
            }
            set
            {
                if (value != _empty)
                {
                    ClearCaches();
                    _empty = value;
                    OnPropertyChanged();
                }
            }
        }

        public override HtmlNodeType NodeType
        {
            get
            {
                return _nodeType;
            }
        }

        internal HtmlElement GetParentToClose(int indent, string name)
        {
            // NOTE: this avoids possible stack overflow errors for "super malformed" documents
            if (indent > 100)
                return null;

            if (name.EqualsIgnoreCase(Name))
                return this;

            if (ParentNode is HtmlElement element)
                return element.GetParentToClose(indent + 1, name);

            return null;
        }

        public virtual bool NoChild
        {
            get
            {
                if (_noChild.HasValue)
                    return _noChild.Value;

                if (OwnerDocument == null)
                    return false;

                return (OwnerDocument.Options.GetElementWriteOptions(Name) & HtmlElementWriteOptions.NoChild) == HtmlElementWriteOptions.NoChild;
            }
            set
            {
                _noChild = value;
            }
        }

        public virtual bool AlwaysClose
        {
            get
            {
                if (_alwaysClose.HasValue)
                    return _alwaysClose.Value;

                if (OwnerDocument == null)
                    return false;

                return (OwnerDocument.Options.GetElementWriteOptions(Name) & HtmlElementWriteOptions.AlwaysClose) == HtmlElementWriteOptions.AlwaysClose;
            }
            set
            {
                _alwaysClose = value;
            }
        }

        public virtual bool DontCloseIfEmpty
        {
            get
            {
                if (_dontCloseIfEmpty.HasValue)
                    return _dontCloseIfEmpty.Value;

                if (OwnerDocument == null)
                    return false;

                return (OwnerDocument.Options.GetElementWriteOptions(Name) & HtmlElementWriteOptions.DontCloseIfEmpty) == HtmlElementWriteOptions.DontCloseIfEmpty;
            }
            set
            {
                _dontCloseIfEmpty = value;
            }
        }

        public override void WriteTo(TextWriter writer)
        {
            writer.Write('<');
            if (IsProcessingInstruction)
            {
                writer.Write('?');
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

            if (HasAttributes)
            {
                foreach (HtmlAttribute attribute in Attributes)
                {
                    writer.Write(' ');
                    attribute.WriteTo(writer);
                }
            }

            bool alwaysClose = AlwaysClose;
            bool dontCloseIfEmpty = DontCloseIfEmpty;
            if ((OwnerDocument?.IsXhtml == true) || alwaysClose)
            {
                dontCloseIfEmpty = false;
            }

            if (IsEmpty && !alwaysClose)
            {
                if (IsProcessingInstruction)
                {
                    writer.Write(" ?>");
                }
                else if (Name.StartsWith('!'))
                {
                    // suc as !DOCTYPE
                    writer.Write('>');
                }
                else if (dontCloseIfEmpty)
                {
                    writer.Write('>');
                }
                else
                {
                    writer.Write(' ');
                    writer.Write(CloseChar);
                    writer.Write('>');
                }
            }
            else
            {
                writer.Write('>');

                if ((!HasChildNodes || NoChild) && dontCloseIfEmpty)
                    return;

                WriteContentTo(writer);

                if (_closed || alwaysClose || (OwnerDocument?.IsXhtml == true))
                {
                    writer.Write("</");
                    writer.Write(Name);
                    writer.Write('>');
                }
            }
        }

        public override void WriteContentTo(TextWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            if (!NoChild)
            {
                if (HasChildNodes)
                {
                    foreach (HtmlNode node in ChildNodes)
                    {
                        node.WriteTo(writer);
                    }
                }
            }
        }

        public override void WriteTo(XmlWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            if (IsDocumentType)
            {
                OwnerDocument?.WriteDocType(writer);
                return;
            }

            writer.WriteStartElement(GetValidXmlName(Prefix), GetValidXmlName(LocalName), NamespaceURI);
            if (HasAttributes)
            {
                foreach (HtmlAttribute attribute in Attributes)
                {
                    if (attribute.Prefix == XmlnsPrefix || attribute.Name == XmlnsPrefix)
                        continue;

                    attribute.WriteTo(writer);
                }
            }

            if (IsEmpty)
            {
                writer.WriteEndElement();
            }
            else
            {
                WriteContentTo(writer);
                writer.WriteFullEndElement();
            }
        }

        public override void WriteContentTo(XmlWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            if (Name.EqualsIgnoreCase("!doctype"))
                return;

            if (!NoChild)
            {
                if (HasChildNodes)
                {
                    foreach (HtmlNode node in ChildNodes)
                    {
                        node.WriteTo(writer);
                    }
                }
            }
        }

        public override void CopyTo(HtmlNode target, HtmlCloneOptions copyOptions)
        {
            base.CopyTo(target, copyOptions);
            var element = (HtmlElement)target;
            element._closed = _closed;
            element._empty = _empty;
            element._processingInstruction = _processingInstruction;
            element._alwaysClose = _alwaysClose;
            element._closeChar = _closeChar;
            element._dontCloseIfEmpty = _dontCloseIfEmpty;
            element._nodeType = _nodeType;
        }
    }
}
