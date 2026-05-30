namespace Meziantou.Framework.Templating;

public sealed class UsingCollection : FreezableCollection<string>
{
    protected override void ValidateItem(string item)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(item);
    }
}
