namespace Meziantou.Framework.Templating;

public sealed class InterfaceCollection : FreezableCollection<string>
{
    protected override void ValidateItem(string item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Length == 0)
            throw new ArgumentException("Value cannot be empty.", nameof(item));
    }
}
