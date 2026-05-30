namespace Meziantou.Framework.Templating;

public sealed class ReferenceCollection : FreezableCollection<string>
{
    protected override void ValidateItem(string item)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(item);
    }
}
