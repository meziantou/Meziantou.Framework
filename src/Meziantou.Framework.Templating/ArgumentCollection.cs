namespace Meziantou.Framework.Templating;

public sealed class ArgumentCollection : FreezableCollection<TemplateArgument>
{
    protected override void ValidateItem(TemplateArgument item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Name);
    }
}
