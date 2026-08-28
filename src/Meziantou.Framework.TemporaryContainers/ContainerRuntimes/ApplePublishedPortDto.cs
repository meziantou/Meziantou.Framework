namespace Meziantou.Framework.TemporaryContainers.Internals;

internal sealed class ApplePublishedPortDto
{
    public int ContainerPort { get; set; }
    public int HostPort { get; set; }
    public string? Proto { get; set; }
}
