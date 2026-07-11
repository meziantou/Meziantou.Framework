namespace Meziantou.Framework.TemporaryContainers.Internals;

internal sealed class DockerNetworkSettingsDto
{
    public string? IPAddress { get; set; }
    public Dictionary<string, List<DockerPortBindingDto>?>? Ports { get; set; }
}
