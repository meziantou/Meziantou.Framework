using System.Reflection;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>Represents a snapshot of a module at a specific point in time.</summary>
public sealed class ModuleSnapshot
{
    internal ModuleSnapshot(Module module)
    {
        Name = module.Name;
    }

    /// <summary>Gets the name of the module.</summary>
    public string Name { get; }
}
