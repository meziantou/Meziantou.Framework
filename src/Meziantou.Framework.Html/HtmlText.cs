#nullable disable
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Xml;

namespace Meziantou.Framework.Html
{
    [DebuggerDisplay("'{Value}'")]
    public sealed class HtmlText : HtmlNode
    {
        private string _value;
        private bool _cData;

        internal HtmlText(HtmlDocument ownerDocument)
            : base(string.Empty, "#text", string.Empty, ownerDocument)
        {
        }

        [Browsable(false)]
        public override HtmlAttributeList Attributes => base.Attributes;

        [Browsable(false)]
        public override HtmlNodeList ChildNodes => base.ChildNodes;

        public override HtmlNodeType NodeType => HtmlNodeType.Text;

        public bool IsWhitespace => string.IsNullOrWhiteSpace(Value);

        public bool IsCData
        {
            get => _cData;
            set
            {
                if (value != _cData)
                {
                    _cData = value;
                    OnPropertyChanged();
                }
            }
        }

        public override string Name
        {
            get => base.Name;
            set
            {
                // do nothing
            }
        }

        public override string InnerText
        {
            get => Value;
            set
            {
                if (!string.Equals(value, Value, StringComparison.Ordinal))
                {
                    Value = value;
                    OnPropertyChanged();
                }
            }
        }

        public override string InnerHtml
        {
            get => Value;

            set
            {
                if (!string.Equals(value, Value, StringComparison.Ordinal))
                {
                    Value = value;
                    OnPropertyChanged();
                }
            }
        }

        public override string Value
        {
            get => _value;
            set
            {
                if (!string.Equals(value, _value, StringComparison.Ordinal))
                {
                    _value = value;
                    OnPropertyChanged();
                }
            }
        }

        public override void WriteTo(TextWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            if (IsCData)
            {
                writer.Write("<![CDATA[");
                writer.Write(Value);
                writer.Write("]]>");
            }
            else
            {
                writer.Write(Value);
            }
        }

        public override void WriteContentTo(TextWriter writer)
        {
        }

        public override void WriteTo(XmlWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            if (IsCData)
            {
                writer.WriteCData(Value);
            }
            else if (IsWhitespace)
            {
                writer.WriteWhitespace(Value);
            }
            else
            {
                writer.WriteString(Value);
            }
        }

        public override void WriteContentTo(XmlWriter writer)
        {
        }

        public override void CopyTo(HtmlNode target, HtmlCloneOptions options)
        {
            base.CopyTo(target, options);
            var text = (HtmlText)target;
            text._cData = _cData;
            text._value = _value;
        }
    }
}
