using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.TemporaryContainers.Internals;

internal static class ContainerRuntimeResolver
{
    public static ContainerRuntime Resolve(ContainerRuntime requested, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(requested);

        if (TryResolve(requested, out var runtime, out _, logger))
            return runtime;

        throw new InvalidOperationException(requested == ContainerRuntime.Auto
            ? "No supported container runtime (Docker Engine API, 'docker', 'podman', 'container', or 'wslc') is available."
            : $"The '{GetExecutableName(requested)}' runtime is not available.");
    }

    public static bool TryResolve(ContainerRuntime requested, out ContainerRuntime runtime, out string executable, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(requested);

        if (requested == ContainerRuntime.Auto)
        {
            // Docker Engine API first — no process spawning overhead
            if (DockerApiRuntime.TryCreate(logger, out runtime))
            {
                executable = "";
                return true;
            }

            // CLI runtimes — use IsSupported() on each candidate for a unified detection path
            foreach (var (candidateRuntime, candidateName) in GetAutoCandidates())
            {
                if (candidateRuntime.IsSupported(logger) && FindExecutable(candidateName) is { } path)
                {
                    runtime = candidateRuntime.Bind(path, logger);
                    executable = path;
                    return true;
                }
            }

            runtime = requested;
            executable = "";
            return false;
        }

        var name = GetExecutableName(requested);
        if (FindExecutable(name) is { } resolved)
        {
            runtime = requested.Bind(resolved, logger);
            executable = resolved;
            return true;
        }

        runtime = requested;
        executable = "";
        return false;
    }

    internal static string? FindExecutable(string name)
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
        if (runtime is ExecutableContainerRuntime exe)
            return exe.ExecutableName;

        throw new ArgumentOutOfRangeException(nameof(runtime), runtime, "Unknown container runtime.");
    }
}
