using System.Globalization;
using System.Text.Json;

namespace Meziantou.Framework.TemporaryContainers.Internals;

internal static class DockerContainerInfoParser
{
    public static ContainerInfo ParseInspectOutput(string output)
    {
        var parsed = JsonSerializer.Deserialize(output, DockerInspectJsonContext.Default.DockerInspectResultArray);
        if (parsed is null || parsed.Length == 0)
            throw new InvalidOperationException("Unable to inspect the container: the runtime returned no data.");

        return ParseInspectResult(parsed[0]);
    }

    /// <summary>Reads an inspect output that may describe several containers. A malformed or empty output means no container, which is what a listing on an empty daemon reports.</summary>
    public static IReadOnlyList<ContainerInfo> ParseInspectOutputs(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return [];

        DockerInspectResult[]? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize(output, DockerInspectJsonContext.Default.DockerInspectResultArray);
        }
        catch (JsonException)
        {
            return [];
        }

        if (parsed is null)
            return [];

        var results = new List<ContainerInfo>(parsed.Length);
        foreach (var result in parsed)
            results.Add(ParseInspectResult(result));

        return results;
    }

    public static ContainerInfo ParseInspectResult(DockerInspectResult result)
    {
        var ports = new Dictionary<int, int>();
        // docker and podman report the bindings under NetworkSettings, wslc at the top level. An empty dictionary in one
        // place does not hide the bindings reported in the other.
        var portBindings = result.NetworkSettings?.Ports is { Count: > 0 } networkPorts ? networkPorts : result.Ports ?? result.NetworkSettings?.Ports;
        if (portBindings is not null)
        {
            foreach (var (key, bindings) in portBindings)
            {
                if (bindings is not { Count: > 0 })
                    continue;

                var slash = key.IndexOf('/', StringComparison.Ordinal);
                var portText = slash >= 0 ? key[..slash] : key;
                if (!int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var containerPort))
                    continue;

                // A port published on every interface is reported once per address family, with the same host port.
                foreach (var binding in bindings)
                {
                    if (int.TryParse(binding.HostPort, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hostPort))
                    {
                        ports.TryAdd(containerPort, hostPort);
                        break;
                    }
                }
            }
        }

        return new ContainerInfo
        {
            Id = result.Id ?? "",
            Name = (result.Name ?? "").TrimStart('/'),
            Image = result.Config?.Image ?? result.Image,
            State = ParseState(result.State?.Status),
            Status = result.State?.Status,
            StartedAt = ParseDate(result.State?.StartedAt),
            FinishedAt = ParseDate(result.State?.FinishedAt),
            ExitCode = result.State is null ? null : unchecked((int)result.State.ExitCode),
            IPAddress = result.NetworkSettings?.IPAddress,
            Ports = ports,
            Labels = result.Config?.Labels ?? result.Labels ?? new Dictionary<string, string>(StringComparer.Ordinal),
            Environment = ParseEnvironment(result.Config?.Env),
        };
    }

    /// <summary>Reads a list of <c>NAME=value</c> entries. A variable listed twice takes the last value, as it does in the process.</summary>
    public static IReadOnlyDictionary<string, string> ParseEnvironment(IEnumerable<string>? variables)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var variable in variables ?? [])
        {
            var separator = variable.IndexOf('=', StringComparison.Ordinal);
            if (separator > 0)
                environment[variable[..separator]] = variable[(separator + 1)..];
        }

        return environment;
    }

    public static ContainerState ParseState(string? status)
    {
        return status switch
        {
            "created" => ContainerState.Created,
            "running" => ContainerState.Running,
            "paused" => ContainerState.Paused,
            "exited" or "dead" => ContainerState.Exited,
            "removing" => ContainerState.Removed,
            _ => ContainerState.Unknown,
        };
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result))
            return null;

        // Runtimes report the zero date for events that never happened (for example FinishedAt on a running container).
        return result.UtcDateTime.Year <= 1 ? null : result;
    }
}
