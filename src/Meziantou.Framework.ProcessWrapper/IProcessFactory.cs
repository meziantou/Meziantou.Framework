using System.Diagnostics;

namespace Meziantou.Framework;

/// <summary>
/// Represents a factory used by <see cref="ProcessWrapper"/> to create process handles.
/// </summary>
public interface IProcessFactory
{
    /// <summary>Creates a process handle from the provided start information.</summary>
    /// <param name="startInfo">The process start information.</param>
    /// <returns>A process handle.</returns>
    IProcessHandle Create(ProcessStartInfo startInfo);
}
