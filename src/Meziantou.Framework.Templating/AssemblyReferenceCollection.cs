using System.Reflection;

namespace Meziantou.Framework.Templating;

public sealed class AssemblyReferenceCollection : FreezableCollection<AssemblyReference>
{
    public void Add(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        Add(AssemblyReference.From(type));
    }

    public void Add(Type type, string alias)
    {
        ArgumentNullException.ThrowIfNull(type);
        Add(type.Assembly, alias);
    }

    public void Add(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        Add(AssemblyReference.From(assembly));
    }

    public void Add(Assembly assembly, string alias)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        Add(new AssemblyReference(assembly.Location, alias));
    }

    public void Add(Module module)
    {
        ArgumentNullException.ThrowIfNull(module);
        Add(AssemblyReference.From(module));
    }

    public void Add(Module module, string alias)
    {
        ArgumentNullException.ThrowIfNull(module);
        Add(module.Assembly, alias);
    }

    public void Add(string path)
    {
        Add(new AssemblyReference(path));
    }

    public void Add(string path, string alias)
    {
        Add(new AssemblyReference(path, alias));
    }

    protected override void ValidateItem(AssemblyReference item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Path);

        if (item.Alias is not null && string.IsNullOrWhiteSpace(item.Alias))
            throw new ArgumentException("Alias cannot be empty or whitespace.", nameof(item));
    }
}
