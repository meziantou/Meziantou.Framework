using System.Reflection;

namespace Meziantou.Framework.PublicApiGenerator;

public sealed class AssemblySource
{
    public AssemblySource(Assembly assembly, string? targetFrameworkMoniker = null)
    {
        Assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
        TargetFrameworkMoniker = targetFrameworkMoniker;
    }

    public AssemblySource(string path, string? targetFrameworkMoniker = null)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
        TargetFrameworkMoniker = targetFrameworkMoniker;
    }

    public Assembly? Assembly { get; }

    public string? Path { get; }

    public string? TargetFrameworkMoniker { get; set; }

    public static implicit operator AssemblySource(string path) => new(path);
    public static implicit operator AssemblySource(Assembly assembly) => new(assembly);
}
