using System.Text.Json;

namespace Meziantou.Framework.TemporaryContainers.Internals;

internal sealed class DockerVolumeInspectResult
{
    public string? Name { get; set; }
    public Dictionary<string, string>? Labels { get; set; }

    /// <summary>Reads the output of a <c>volume inspect</c> command. A malformed or empty output means no volume, which is what a cleanup on an empty daemon reports.</summary>
    public static IReadOnlyList<ManagedResource> Parse(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return [];

        DockerVolumeInspectResult[]? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize(output, DockerInspectJsonContext.Default.DockerVolumeInspectResultArray);
        }
        catch (JsonException)
        {
            return [];
        }

        if (parsed is null)
            return [];

        var resources = new List<ManagedResource>(parsed.Length);
        foreach (var volume in parsed)
        {
            if (!string.IsNullOrEmpty(volume.Name))
                resources.Add(new ManagedResource(volume.Name, volume.Labels ?? new Dictionary<string, string>(StringComparer.Ordinal)));
        }

        return resources;
    }
}
