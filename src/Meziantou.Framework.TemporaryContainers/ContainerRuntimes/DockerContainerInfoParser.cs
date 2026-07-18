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

    public static ContainerInfo ParseInspectResult(DockerInspectResult result)
    {
        var ports = new Dictionary<int, int>();
        var portBindings = result.NetworkSettings?.Ports ?? result.Ports;
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

                if (int.TryParse(bindings[0].HostPort, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hostPort))
                    ports[containerPort] = hostPort;
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
        };
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

        return result.UtcDateTime.Year <= 1 ? null : result;
    }
}
