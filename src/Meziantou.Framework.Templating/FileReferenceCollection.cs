namespace Meziantou.Framework.Templating;

public sealed class FileReferenceCollection : FreezableCollection<FileReference>
{
    public void Add(string path)
    {
        Add(new FileReference(path));
    }

    protected override void ValidateItem(FileReference item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Path);
    }
}
