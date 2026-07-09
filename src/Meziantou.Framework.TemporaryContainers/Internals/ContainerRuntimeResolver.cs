namespace Meziantou.Framework.TemporaryContainers.Internals;

internal static class ContainerRuntimeResolver
{
    public static (ContainerRuntime Runtime, string Executable) Resolve(ContainerRuntime requested)
    {
        if (TryResolve(requested, out var runtime, out var executable))
            return (runtime, executable);

        throw new InvalidOperationException(requested is ContainerRuntime.Auto
            ? "No supported container runtime ('docker', 'podman', 'container', or 'wslc') is available."
            : $"The '{GetExecutableName(requested)}' runtime is not available.");
    }

    public static bool TryResolve(ContainerRuntime requested, out ContainerRuntime runtime, out string executable)
    {
        if (requested is ContainerRuntime.Auto)
        {
            foreach (var (candidateRuntime, candidateName) in GetAutoCandidates())
            {
                if (FindExecutable(candidateName) is { } path)
                {
                    runtime = candidateRuntime;
                    executable = path;
                    return true;
                }
            }

            runtime = default;
            executable = "";
            return false;
        }

        var name = GetExecutableName(requested);
        if (FindExecutable(name) is { } resolved)
        {
            runtime = requested;
            executable = resolved;
            return true;
        }

        runtime = default;
        executable = "";
        return false;
    }

    private static string? FindExecutable(string name)
    {
        // On Windows a runtime such as Docker Desktop ships both an extensionless shim and the real
        // '.exe'; the shim cannot be launched by Process.Start, so prefer an executable extension.
        if (OperatingSystem.IsWindows())
        {
            foreach (var extension in (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (ExecutableFinder.GetFullExecutablePath(name + extension) is { } withExtension)
                    return withExtension;
            }
        }

        return ExecutableFinder.GetFullExecutablePath(name);
    }

    private static IEnumerable<(ContainerRuntime Runtime, string Name)> GetAutoCandidates()
    {
        yield return (ContainerRuntime.Docker, "docker");
        yield return (ContainerRuntime.Podman, "podman");

        if (OperatingSystem.IsMacOS())
            yield return (ContainerRuntime.AppleContainer, "container");

        if (OperatingSystem.IsWindows())
            yield return (ContainerRuntime.Wslc, "wslc");
    }

    private static string GetExecutableName(ContainerRuntime runtime)
    {
        return runtime switch
        {
            ContainerRuntime.Docker => "docker",
            ContainerRuntime.Podman => "podman",
            ContainerRuntime.AppleContainer => "container",
            ContainerRuntime.Wslc => "wslc",
            _ => throw new ArgumentOutOfRangeException(nameof(runtime), runtime, "Unknown container runtime."),
        };
    }
}
