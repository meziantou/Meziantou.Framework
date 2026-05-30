namespace Meziantou.Framework.Templating;

public sealed class InterfaceCollection : FreezableCollection<string>
{
    protected override void ValidateItem(string item)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(item);
    }
}
