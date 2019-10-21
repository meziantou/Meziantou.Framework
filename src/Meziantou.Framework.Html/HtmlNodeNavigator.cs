﻿#nullable disable
//#define HTML_XPATH_TRACE
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.XPath;

namespace Meziantou.Framework.Html
{
    public sealed class HtmlNodeNavigator : XPathNavigator
    {
        private readonly NameTable _nameTable = new NameTable();
        private HtmlNode _currentNode;
        private readonly HtmlNode _rootNode;

        [Conditional("HTML_XPATH_TRACE")]
        private static void Trace(object value, [CallerMemberName] string methodName = null)
        {
#if HTML_XPATH_TRACE
            if (!EnableTrace)
                return;
#endif

            Debug.WriteLine(methodName + ":" + value);
        }

#if HTML_XPATH_TRACE
        internal static bool EnableTrace { get; set; }
#endif

        public HtmlNodeNavigator(HtmlDocument document, HtmlNode currentNode, HtmlNodeNavigatorOptions options)
        {
            if (currentNode == null)
                throw new ArgumentNullException(nameof(currentNode));

            Document = document;
            CurrentNode = currentNode;
            BaseNode = currentNode;
            Options = options;
            if ((options & HtmlNodeNavigatorOptions.RootNode) == HtmlNodeNavigatorOptions.RootNode)
            {
                _rootNode = CurrentNode;
            }
        }

        private HtmlNodeNavigator(HtmlNodeNavigator other)
        {
            if (other == null)
                throw new ArgumentNullException(nameof(other));

            CurrentNode = other.CurrentNode;
            BaseNode = other.BaseNode;
            Document = other.Document;
            Options = other.Options;
            _rootNode = other._rootNode;
        }

        public override object UnderlyingObject => CurrentNode;

        public HtmlNode CurrentNode
        {
            get => _currentNode;
            set
            {
                if (_currentNode != value)
                {
                    Trace("old: " + _currentNode + " new: " + value);
                    _currentNode = value;
                }
            }
        }

        public HtmlNodeNavigatorOptions Options { get; }
        public HtmlDocument Document { get; }
        public HtmlNode BaseNode { get; }

        private string GetOrAdd(string array)
        {
            return _nameTable.Get(array) ?? _nameTable.Add(array);
        }

        public override string BaseURI => GetOrAdd(string.Empty);

        public override XPathNavigator Clone() => new HtmlNodeNavigator(this);

        public override string OuterXml
        {
            get
            {
                if (CurrentNode == null)
                    return null;

                return CurrentNode.OuterXml;
            }
            set => base.OuterXml = value;
        }

        public override bool IsEmptyElement
        {
            get
            {
                var element = CurrentNode as HtmlElement;
                Trace("=" + (element?.IsEmpty == true));
                return element?.IsEmpty == true;
            }
        }

        public override bool IsSamePosition(XPathNavigator other)
        {
            var nav = other as HtmlNodeNavigator;
            if (nav == null)
                return false;

            if (Document != null)
                return Document == nav.Document && CurrentNode == nav.CurrentNode;

            return BaseNode == nav.BaseNode && CurrentNode == nav.CurrentNode;
        }

        public override bool MoveTo(XPathNavigator other)
        {
            var nav = other as HtmlNodeNavigator;
            Trace("nav:" + nav);
            if (nav == null || (Document != null && nav.Document != Document) || (BaseNode != nav.BaseNode))
                return false;

            CurrentNode = nav.CurrentNode;
            return true;
        }

        public override bool MoveToFirstAttribute()
        {
            var element = CurrentNode as HtmlElement;
            Trace("element:" + element);
            if (element == null)
                return false;

            Trace("element.HasAttributes:" + element.HasAttributes);
            if (!element.HasAttributes)
                return false;

            CurrentNode = element.Attributes[0];
            return true;
        }

        public override bool MoveToFirstChild()
        {
            Trace("ChildNodes.HasChildNodes:" + CurrentNode.HasChildNodes);
            if (!CurrentNode.HasChildNodes)
                return false;

            CurrentNode = CurrentNode.ChildNodes[0];
            return true;
        }

        private static HtmlAttribute MoveToFirstNamespaceLocal(HtmlAttributeList attributes)
        {
            if (attributes == null)
                return null;

            foreach (var att in attributes)
            {
                if (att.IsNamespace)
                    return att;
            }
            return null;
        }

        private static HtmlAttribute MoveToFirstNamespaceGlobal(HtmlNode rootNode, ref HtmlAttributeList attributes)
        {
            var att = MoveToFirstNamespaceLocal(attributes);
            if (att != null)
                return att;

            if (rootNode != null && attributes != null && attributes.Parent == rootNode)
                return null;

            var element = attributes != null ? attributes.Parent.ParentNode as HtmlElement : null;
            while (element != null)
            {
                if (rootNode != null && element.Equals(rootNode))
                    return null;

                if (element.HasAttributes)
                {
                    attributes = element.Attributes;
                    att = MoveToFirstNamespaceLocal(attributes);
                }
                if (att != null)
                    return att;

                element = element.ParentNode as HtmlElement;
            }
            return null;
        }

        public override bool MoveToFirstNamespace(XPathNamespaceScope namespaceScope)
        {
            var element = CurrentNode as HtmlElement;
            if (element == null)
                return false;

            HtmlAttribute att = null;
            HtmlAttributeList attributes = null;
            switch (namespaceScope)
            {
                case XPathNamespaceScope.Local:
                    if (element.HasAttributes)
                    {
                        att = MoveToFirstNamespaceLocal(element.Attributes);
                    }
                    if (att == null)
                        return false;

                    CurrentNode = att;
                    break;

                case XPathNamespaceScope.ExcludeXml:
                    if (element.HasAttributes)
                    {
                        attributes = element.Attributes;
                        att = MoveToFirstNamespaceGlobal(_rootNode, ref attributes);
                    }
                    if (att == null)
                        return false;

                    while (string.Equals(att.LocalName, HtmlNode.XmlPrefix, StringComparison.Ordinal))
                    {
                        att = MoveToNextNamespaceGlobal(_rootNode, ref attributes, att);
                        if (att == null)
                            return false;
                    }
                    CurrentNode = att;
                    break;

                case XPathNamespaceScope.All:
                default:
                    if (element.HasAttributes)
                    {
                        attributes = element.Attributes;
                        att = MoveToFirstNamespaceGlobal(_rootNode, ref attributes);
                    }
                    if (att == null)
                    {
                        if (Document == null)
                            return false;

                        CurrentNode = Document.NamespaceXml;
                    }
                    else
                    {
                        CurrentNode = att;
                    }
                    break;
            }

            return true;
        }

        private static HtmlAttribute MoveToNextNamespaceLocal(HtmlAttribute att)
        {
            att = att.NextSibling;
            while (att != null)
            {
                if (att.IsNamespace)
                    return att;

                att = att.NextSibling;
            }

            return null;
        }

        private static HtmlAttribute MoveToNextNamespaceGlobal(HtmlNode rootNode, ref HtmlAttributeList attributes, HtmlAttribute att)
        {
            var next = MoveToNextNamespaceLocal(att);
            if (next != null)
                return next;

            if (rootNode != null && attributes != null && attributes.Parent == rootNode)
                return null;

            var element = attributes != null ? attributes.Parent.ParentNode as HtmlElement : null;
            while (element != null)
            {
                if (rootNode != null && element.Equals(rootNode))
                    return null;

                if (element.HasAttributes)
                {
                    attributes = element.Attributes;
                    next = MoveToFirstNamespaceLocal(attributes);
                    if (next != null)
                        return next;
                }
                element = element.ParentNode as HtmlElement;
            }
            return null;
        }

        public override bool MoveToNextNamespace(XPathNamespaceScope namespaceScope)
        {
            var attribute = CurrentNode as HtmlAttribute;
            if (attribute == null || !attribute.IsNamespace)
                return false;

            HtmlAttribute att;
            var attributes = attribute.ParentNode.HasAttributes ? attribute.ParentNode.Attributes : null;
            switch (namespaceScope)
            {
                case XPathNamespaceScope.Local:
                    att = MoveToNextNamespaceLocal(attribute);
                    if (att == null)
                        return false;

                    CurrentNode = att;
                    break;

                case XPathNamespaceScope.ExcludeXml:
                    att = attribute;
                    do
                    {
                        att = MoveToNextNamespaceGlobal(_rootNode, ref attributes, att);
                        if (att == null)
                            return false;
                    }
                    while (string.Equals(att.LocalName, HtmlNode.XmlPrefix, StringComparison.Ordinal));
                    CurrentNode = att;
                    break;

                case XPathNamespaceScope.All:
                default:
                    att = attribute;
                    do
                    {
                        att = MoveToNextNamespaceGlobal(_rootNode, ref attributes, att);
                        if (att == null)
                        {
                            if (Document == null)
                                return false;

                            CurrentNode = Document.NamespaceXml;
                            return true;
                        }
                    }
                    while (string.Equals(att.LocalName, HtmlNode.XmlPrefix, StringComparison.Ordinal));
                    CurrentNode = att;
                    break;
            }
            return true;
        }

        public override bool MoveToNext()
        {
            var node = CurrentNode.NextSibling;
            Trace("node:" + node);
            if (node == null)
                return false;

            CurrentNode = node;
            return true;
        }

        public override bool MoveToNextAttribute()
        {
            var att = CurrentNode as HtmlAttribute;
            Trace("att:" + att);
            if (att == null)
                return false;

            HtmlNode node = att.NextSibling;
            Trace("next att:" + node);
            if (node == null)
                return false;

            CurrentNode = node;
            return true;
        }

        public override bool MoveToParent()
        {
            Trace("ParentNode:" + CurrentNode.ParentNode);
            if (CurrentNode.ParentNode == null)
                return false;

            if (_rootNode != null && CurrentNode.ParentNode == _rootNode)
            {
                Trace("ParentNode reached root");
                return false;
            }

            CurrentNode = CurrentNode.ParentNode;
            return true;
        }

        public override bool MoveToPrevious()
        {
            var node = CurrentNode.PreviousSibling;
            Trace("PreviousSibling:" + node);
            if (node == null)
                return false;

            CurrentNode = node;
            return true;
        }

        public override void MoveToRoot()
        {
            Trace(value: null);
            CurrentNode = Document ?? BaseNode;
        }

        public override bool MoveToId(string id)
        {
            throw new NotSupportedException();
        }

        public override string LocalName
        {
            get
            {
                var name = CurrentNode.LocalName;
                Trace("=" + name);
                if (name != null)
                {
                    if ((Options & HtmlNodeNavigatorOptions.UppercasedNames) == HtmlNodeNavigatorOptions.UppercasedNames)
                        return name.ToUpperInvariant();

                    if ((Options & HtmlNodeNavigatorOptions.LowercasedNames) == HtmlNodeNavigatorOptions.LowercasedNames)
                        return name.ToLowerInvariant();
                }

                return name ?? string.Empty;
            }
        }

        public override string Name
        {
            get
            {
                var name = CurrentNode.Name;
                Trace("=" + name);
                if (name != null)
                {
                    if ((Options & HtmlNodeNavigatorOptions.UppercasedNames) == HtmlNodeNavigatorOptions.UppercasedNames)
                        return name.ToUpperInvariant();

                    if ((Options & HtmlNodeNavigatorOptions.LowercasedNames) == HtmlNodeNavigatorOptions.LowercasedNames)
                        return name.ToLowerInvariant();
                }

                return name ?? string.Empty;
            }
        }

        public override XmlNameTable NameTable => _nameTable;

        public override string NamespaceURI
        {
            get
            {
                var ns = CurrentNode.NamespaceURI;
                if (Document?.Options.EmptyNamespacesForXPath.Contains(ns) == true)
                    return string.Empty;

                Debug.Assert(ns != null);
                if ((Options & HtmlNodeNavigatorOptions.UppercasedNamespaceURIs) == HtmlNodeNavigatorOptions.UppercasedNamespaceURIs)
                    return ns.ToUpperInvariant();

                if ((Options & HtmlNodeNavigatorOptions.LowercasedNamespaceURIs) == HtmlNodeNavigatorOptions.LowercasedNamespaceURIs)
                    return ns.ToLowerInvariant();

                Trace("=" + ns);
                return ns ?? string.Empty;
            }
        }

        public override XPathNodeType NodeType
        {
            get
            {
                XPathNodeType nt;
                switch (CurrentNode.NodeType)
                {
                    case HtmlNodeType.Attribute:
                        nt = XPathNodeType.Attribute;
                        break;

                    case HtmlNodeType.Comment:
                        nt = XPathNodeType.Comment;
                        break;

                    case HtmlNodeType.Document:
                        nt = XPathNodeType.Root;
                        break;

                    case HtmlNodeType.DocumentType:
                    case HtmlNodeType.Element:
                        //case HtmlNodeType.EndElement:
                        nt = XPathNodeType.Element;
                        break;

                    case HtmlNodeType.ProcessingInstruction:
                        nt = XPathNodeType.ProcessingInstruction;
                        break;

                    case HtmlNodeType.None:
                    case HtmlNodeType.Text:
                    default:
                        nt = XPathNodeType.Text;
                        break;
                }
                Trace("=" + nt);
                return nt;
            }
        }

        public override string Prefix
        {
            get
            {
                Debug.Assert(CurrentNode.Prefix != null);
                var prefix = CurrentNode.Prefix;
                Trace("=" + prefix);
                if ((Options & HtmlNodeNavigatorOptions.UppercasedPrefixes) == HtmlNodeNavigatorOptions.UppercasedPrefixes)
                    return prefix.ToUpperInvariant();

                if ((Options & HtmlNodeNavigatorOptions.LowercasedPrefixes) == HtmlNodeNavigatorOptions.LowercasedPrefixes)
                    return prefix.ToLowerInvariant();

                return prefix ?? string.Empty;
            }
        }

        public override string Value
        {
            get
            {
                Trace("=" + CurrentNode.Value);
                if (CurrentNode is HtmlElement element)
                {
                    if ((Options & HtmlNodeNavigatorOptions.UppercasedValues) == HtmlNodeNavigatorOptions.UppercasedValues)
                        return element.InnerText.ToUpperInvariant();

                    if ((Options & HtmlNodeNavigatorOptions.LowercasedValues) == HtmlNodeNavigatorOptions.LowercasedValues)
                        return element.InnerText.ToLowerInvariant();

                    return element.InnerText;
                }

                var value = CurrentNode.Value;
                if (value != null)
                {
                    if ((Options & HtmlNodeNavigatorOptions.UppercasedValues) == HtmlNodeNavigatorOptions.UppercasedValues)
                        return value.ToUpperInvariant();

                    if ((Options & HtmlNodeNavigatorOptions.LowercasedValues) == HtmlNodeNavigatorOptions.LowercasedValues)
                        return value.ToLowerInvariant();
                }

                return value;
            }
        }
    }
}
