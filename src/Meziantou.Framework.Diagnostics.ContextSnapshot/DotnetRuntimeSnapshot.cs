using System.Runtime.InteropServices;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>Represents a snapshot of the .NET runtime including the runtime identifier, framework description, and Mono detection.</summary>
public sealed class DotnetRuntimeSnapshot
{
    public string? RuntimeIdentifier { get; } = RuntimeInformation.RuntimeIdentifier;
    public string? FrameworkDescription { get; } = RuntimeInformation.FrameworkDescription;
    public bool IsMono { get; } = IsMonoStatic;

    internal static bool IsFullFramework =>
#if NET6_0_OR_GREATER
        false;
#else
        FrameworkDescription.StartsWith(".NET Framework", StringComparison.OrdinalIgnoreCase);
#endif

    internal static bool IsMonoStatic { get; } = Type.GetType("Mono.RuntimeStructs") != null;
}
