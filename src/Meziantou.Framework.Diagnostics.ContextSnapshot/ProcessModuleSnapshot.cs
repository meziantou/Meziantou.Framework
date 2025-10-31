using System.Diagnostics;
using Meziantou.Framework.Diagnostics.ContextSnapshot.Internals;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>Represents a snapshot of a process module at a specific point in time.</summary>
public sealed class ProcessModuleSnapshot
{
    internal ProcessModuleSnapshot(ProcessModule module)
    {
        ModuleName = Utils.SafeGet(() => module.ModuleName);
        FileName = Utils.SafeGet(() => module.FileName);
        ProductVersion = Utils.SafeGet(() => module.FileVersionInfo.ProductVersion);
        FileVersion = Utils.SafeGet(() => module.FileVersionInfo.FileVersion);
    }

    /// <summary>Gets the name of the module.</summary>
    public string ModuleName { get; }
    /// <summary>Gets the full path to the module file.</summary>
    public string FileName { get; }
    /// <summary>Gets the product version of the module.</summary>
    public string? ProductVersion { get; }
    /// <summary>Gets the file version of the module.</summary>
    public string? FileVersion { get; }
}
