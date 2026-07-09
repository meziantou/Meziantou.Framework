namespace Meziantou.Framework.TemporaryContainers.Internals;

internal sealed class DockerInspectResult
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Image { get; set; }
    public Dictionary<string, string>? Labels { get; set; }
    public Dictionary<string, List<DockerPortBindingDto>?>? Ports { get; set; }
    public DockerStateDto? State { get; set; }
    public DockerConfigDto? Config { get; set; }
    public DockerNetworkSettingsDto? NetworkSettings { get; set; }
}
