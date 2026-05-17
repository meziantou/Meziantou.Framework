using System.Runtime.InteropServices;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>Represents a snapshot of the .NET runtime including the runtime identifier, framework description, and Mono detection.</summary>
public sealed class DotnetRuntimeSnapshot
{
    public string? RuntimeIdentifier { get; } = RuntimeInformation.RuntimeIdentifier;
    public string? FrameworkDescription { get; } = RuntimeInformation.FrameworkDescription;
    public bool IsMono { get; } = IsMonoStatic;

    internal static bool IsFullFramework => false;

    internal static bool IsMonoStatic { get; } = Type.GetType("Mono.RuntimeStructs") != null;
}
