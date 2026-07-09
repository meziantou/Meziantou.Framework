using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

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
        // Some runtimes can have a CLI on PATH while their backend is unavailable.
        // Probe readiness so tests skip unavailable runtimes instead of failing when creating containers.
        if (runtime is ContainerRuntime.Docker or ContainerRuntime.Podman or ContainerRuntime.Wslc)
            return IsDockerLikeRuntimeOperational(executable);

        // On macOS, the `container` executable can exist while its backend services are stopped.
        // In that state every command fails with an XPC connection error and tests should be skipped.
        if (runtime is not ContainerRuntime.AppleContainer)
            return true;

        if (!TryRunProbe(executable, ["system", "status", "--format", "json"], out var jsonResult))
            return false;

        if (jsonResult.ExitCode != 0)
            return false;

        if (TryGetAppleContainerStatusFromJson(jsonResult.StandardOutput, out var status))
            return string.Equals(status, "running", StringComparison.OrdinalIgnoreCase);

        // Fall back to the default output in case older versions don't honor --format json.
        if (!TryRunProbe(executable, ["system", "status"], out var tableResult))
            return false;

        if (tableResult.ExitCode != 0)
            return false;

        return TryGetAppleContainerStatusFromTable(tableResult.StandardOutput, out status)
            && string.Equals(status, "running", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDockerLikeRuntimeOperational(string executable)
    {
        if (TryRunProbe(executable, ["info", "--format", "json"], out var jsonResult))
            return jsonResult.ExitCode == 0;

        // Older runtimes may not support --format json.
        if (TryRunProbe(executable, ["info"], out var infoResult))
            return infoResult.ExitCode == 0;

        return false;
    }

    private static bool TryRunProbe(string executable, string[] arguments, out ProbeResult result)
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
            {
                result = default;
                return false;
            }

            var standardOutputTask = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                result = default;
                return false;
            }

            result = new ProbeResult(process.ExitCode, standardOutputTask.GetAwaiter().GetResult());
            return true;
        }
        catch
        {
            result = default;
            return false;
        }
    }

    private static bool TryGetAppleContainerStatusFromJson(string json, [NotNullWhen(true)] out string? status)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("status", out var statusProperty) && statusProperty.ValueKind is JsonValueKind.String)
            {
                status = statusProperty.GetString();
                return status is not null;
            }
        }
        catch
        {
        }

        status = null;
        return false;
    }

    private static bool TryGetAppleContainerStatusFromTable(string output, [NotNullWhen(true)] out string? status)
    {
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.StartsWith("status", StringComparison.OrdinalIgnoreCase))
                continue;

            var lastWhitespaceIndex = line.LastIndexOfAny([' ', '\t']);
            if (lastWhitespaceIndex <= 0 || lastWhitespaceIndex >= line.Length - 1)
                continue;

            status = line[(lastWhitespaceIndex + 1)..].Trim();
            return status.Length > 0;
        }

        status = null;
        return false;
    }

    private readonly record struct ProbeResult(int ExitCode, string StandardOutput);

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
