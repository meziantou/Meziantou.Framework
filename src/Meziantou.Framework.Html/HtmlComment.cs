using System;
using System.Diagnostics;
using System.IO;
using System.Xml;

namespace Meziantou.Framework.Html
{
    [DebuggerDisplay("'{Value}'")]
    public class HtmlComment : HtmlNode
    {
        private string _value;

        protected internal HtmlComment(HtmlDocument ownerDocument)
            : base(string.Empty, "#comment", string.Empty, ownerDocument)
        {
        }

        public override HtmlNodeType NodeType => HtmlNodeType.Comment;

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
                if (value != Value)
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
                if (value != _value)
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

            writer.Write("<!--");
            writer.Write(Value);
            writer.Write("-->");
        }

        public override void WriteContentTo(TextWriter writer)
        {
        }

        public override void WriteTo(XmlWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            writer.WriteComment(Value);
        }

        public override void WriteContentTo(XmlWriter writer)
        {
        }

        public override void CopyTo(HtmlNode target, HtmlCloneOptions copyOptions)
        {
            base.CopyTo(target, copyOptions);
            var comment = (HtmlComment)target;
            comment._value = _value;
        }
    }
}
