namespace Meziantou.Framework.Templating;

public sealed class BlockCollection : FreezableCollection<TemplateBlock>
{
    protected override void ValidateItem(TemplateBlock item)
    {
        ArgumentNullException.ThrowIfNull(item);
    }
}
