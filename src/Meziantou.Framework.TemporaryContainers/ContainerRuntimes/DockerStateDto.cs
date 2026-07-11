namespace Meziantou.Framework.TemporaryContainers.Internals;

internal sealed class DockerStateDto
{
    public string? Status { get; set; }
    public bool Running { get; set; }
    public bool Paused { get; set; }
    public uint ExitCode { get; set; }
    public string? StartedAt { get; set; }
    public string? FinishedAt { get; set; }
}
