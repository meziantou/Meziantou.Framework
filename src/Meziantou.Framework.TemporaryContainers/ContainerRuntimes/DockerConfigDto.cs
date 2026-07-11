namespace Meziantou.Framework.TemporaryContainers.Internals;

internal sealed class DockerConfigDto
{
    public string? Image { get; set; }
    public Dictionary<string, string>? Labels { get; set; }
}
