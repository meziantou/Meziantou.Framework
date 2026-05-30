namespace Meziantou.Framework.Templating;

public sealed class ArgumentCollection : FreezableCollection<TemplateArgument>
{
    protected override void ValidateItem(TemplateArgument item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (string.IsNullOrEmpty(item.Name))
            throw new ArgumentException("Argument name cannot be null or empty.", nameof(item));
    }
}
