using System.Reflection;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

public sealed class ModuleSnapshot
{
    internal ModuleSnapshot(Module module)
    {
        Name = module.Name;
    }

    public string Name { get; }
}
