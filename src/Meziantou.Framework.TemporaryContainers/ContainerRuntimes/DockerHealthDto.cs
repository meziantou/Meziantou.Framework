namespace Meziantou.Framework.TemporaryContainers.Internals;

internal sealed class DockerHealthDto
{
    public string? Status { get; set; }
    public List<DockerHealthLogDto>? Log { get; set; }
}
