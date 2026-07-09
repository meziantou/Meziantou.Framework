using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

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
                    if (!IsRuntimeOperational(candidateRuntime, path))
                        continue;

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
            if (!IsRuntimeOperational(requested, resolved))
            {
                runtime = default;
                executable = "";
                return false;
            }

            runtime = requested;
            executable = resolved;
            return true;
        }

        runtime = default;
        executable = "";
        return false;
    }

    private static bool IsRuntimeOperational(ContainerRuntime runtime, string executable)
    {
        // On macOS, the `container` executable can exist while its backend services are stopped.
        // In that state every command fails with an XPC connection error and tests should be skipped.
        return runtime is not ContainerRuntime.AppleContainer || ProbeExecutable(executable, "system", "status");
    }

    private static bool ProbeExecutable(string executable, params string[] arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };

            foreach (var argument in arguments)
                process.StartInfo.ArgumentList.Add(argument);

            if (!process.Start())
                return false;

            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
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
