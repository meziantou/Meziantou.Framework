namespace Meziantou.Framework.Templating
{
    internal sealed class HtmlEmailSection
    {
        public string Name { get; }
        public StringWriter Writer { get; }

        public HtmlEmailSection(string name, StringWriter writer)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }
    }
}
