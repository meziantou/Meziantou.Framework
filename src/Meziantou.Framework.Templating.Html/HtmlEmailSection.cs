namespace Meziantou.Framework.Templating;

internal sealed class HtmlEmailSection
{
    public string Name { get; }
    public StringWriter Writer { get; }

    public HtmlEmailSection(string name!!, StringWriter writer!!)
    {
        Name = name;
        Writer = writer;
    }
}
