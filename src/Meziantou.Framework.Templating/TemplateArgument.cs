namespace Meziantou.Framework.Templating;

public class TemplateArgument
{
    public TemplateArgument(string name!!, Type? type)
    {
        Name = name;
        Type = type;
    }

    public string Name { get; }
    public Type? Type { get; }
}
