using System.Diagnostics;
using Meziantou.Framework.Diagnostics.ContextSnapshot.Internals;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>Represents a snapshot of a process module including file name, product version, and file version.</summary>
public sealed class ProcessModuleSnapshot
{
    internal ProcessModuleSnapshot(ProcessModule module)
    {
        ModuleName = Utils.SafeGet(() => module.ModuleName);
        FileName = Utils.SafeGet(() => module.FileName);
        ProductVersion = Utils.SafeGet(() => module.FileVersionInfo.ProductVersion);
        FileVersion = Utils.SafeGet(() => module.FileVersionInfo.FileVersion);
    }

    public string ModuleName { get; }
    public string FileName { get; }
    public string? ProductVersion { get; }
    public string? FileVersion { get; }
}
