using System.Runtime.InteropServices;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>Represents a snapshot of .NET runtime information at a specific point in time.</summary>
public sealed class DotnetRuntimeSnapshot
{
    /// <summary>Gets the runtime identifier of the .NET runtime.</summary>
    public string? RuntimeIdentifier { get; } = RuntimeInformation.RuntimeIdentifier;
    /// <summary>Gets a description of the .NET framework.</summary>
    public string? FrameworkDescription { get; } = RuntimeInformation.FrameworkDescription;
    /// <summary>Gets a value indicating whether the runtime is Mono.</summary>
    public bool IsMono { get; } = IsMonoStatic;

    internal static bool IsFullFramework =>
#if NET6_0_OR_GREATER
        false;
#else
        FrameworkDescription.StartsWith(".NET Framework", StringComparison.OrdinalIgnoreCase);
#endif

    internal static bool IsMonoStatic { get; } = Type.GetType("Mono.RuntimeStructs") != null;
}
