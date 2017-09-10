using System;
using System.IO;

namespace Meziantou.Framework.Templating
{
    internal class HtmlEmailSection
    {
        public string Name { get; }
        public StringWriter Writer { get; }

        public HtmlEmailSection(string name, StringWriter writer)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            Name = name;
            Writer = writer;
        }
    }
}