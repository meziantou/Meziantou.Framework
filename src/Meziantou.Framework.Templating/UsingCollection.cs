namespace Meziantou.Framework.Templating;

public sealed class UsingCollection : FreezableCollection<string>
{
    protected override void ValidateItem(string item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Length == 0)
            throw new ArgumentException("Value cannot be empty.", nameof(item));
    }
}
