using System;

namespace Meziantou.Framework.Templating
{
    public class TemplateArgument
    {
        public TemplateArgument(string name, Type type)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));

            Name = name;
            Type = type;
        }

        public string Name { get; }
        public Type Type { get; }
    }
}