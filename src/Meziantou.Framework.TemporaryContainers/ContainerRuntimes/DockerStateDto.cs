namespace Meziantou.Framework.TemporaryContainers.Internals;

internal sealed class DockerStateDto
{
    public string? Status { get; set; }
    public bool Running { get; set; }
    public bool Paused { get; set; }
    public uint ExitCode { get; set; }
    public string? StartedAt { get; set; }
    public string? FinishedAt { get; set; }
    public DockerHealthDto? Health { get; set; }

    // podman reports the health under this name before version 4.3.
    public DockerHealthDto? Healthcheck { get; set; }
}
