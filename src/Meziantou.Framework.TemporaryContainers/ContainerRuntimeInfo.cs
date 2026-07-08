using Meziantou.Framework.TemporaryContainers.Internals;

namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Helpers to query container runtime availability (for example to skip tests when no runtime is installed).</summary>
public static class ContainerRuntimeInfo
{
    /// <summary>Determines whether a container runtime can be resolved.</summary>
    /// <param name="runtime">The runtime to look for, or <see cref="ContainerRuntime.Auto"/> to accept any supported runtime.</param>
    /// <returns><see langword="true"/> if the runtime executable is available on PATH; otherwise, <see langword="false"/>.</returns>
    public static bool IsAvailable(ContainerRuntime runtime = ContainerRuntime.Auto)
    {
        return ContainerRuntimeResolver.TryResolve(runtime, out _, out _);
    }

    /// <summary>Gets the runtime that would be used when <see cref="ContainerRuntime.Auto"/> is requested.</summary>
    /// <param name="runtime">When this method returns, contains the resolved runtime if one was found.</param>
    /// <returns><see langword="true"/> if a runtime was found; otherwise, <see langword="false"/>.</returns>
    public static bool TryGetAvailableRuntime(out ContainerRuntime runtime)
    {
        return ContainerRuntimeResolver.TryResolve(ContainerRuntime.Auto, out runtime, out _);
    }
}
