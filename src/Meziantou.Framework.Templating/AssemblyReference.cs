using System.Reflection;

namespace Meziantou.Framework.Templating;

public sealed record class AssemblyReference
{
    public AssemblyReference(string path, string? alias = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (alias is not null && string.IsNullOrWhiteSpace(alias))
            throw new ArgumentException("Alias cannot be empty or whitespace.", nameof(alias));

        Path = path;
        Alias = alias;
    }

    public string Path { get; }
    public string? Alias { get; }

    public static AssemblyReference From(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return From(type.Assembly);
    }

    public static AssemblyReference From(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return new AssemblyReference(assembly.Location);
    }

    public static AssemblyReference From(Module module)
    {
        ArgumentNullException.ThrowIfNull(module);
        return From(module.Assembly);
    }
}
