using System.Reflection;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>Represents a snapshot of a .NET module.</summary>
public sealed class ModuleSnapshot
{
    internal ModuleSnapshot(Module module)
    {
        Name = module.Name;
    }

    public string Name { get; }
}
