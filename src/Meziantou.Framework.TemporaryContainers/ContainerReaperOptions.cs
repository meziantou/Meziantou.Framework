namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Configures the watchdog container started by <see cref="ContainerRuntime.StartReaperAsync(ContainerReaperOptions, CancellationToken)"/>.</summary>
public sealed class ContainerReaperOptions
{
    /// <summary>Gets or sets the image of the watchdog. Defaults to <c>testcontainers/ryuk</c>, whose protocol this implementation speaks.</summary>
    public ImageSource Image { get; set; } = ImageSource.FromRegistry("testcontainers/ryuk:0.14.0");

    /// <summary>Gets or sets the path of the daemon socket, as the daemon sees it, which is mounted into the watchdog so it can remove the resources. Defaults to the socket the runtime reports: the one it connects to on Linux (rootless daemons included), the socket of the podman service, and <c>/var/run/docker.sock</c> inside the virtual machine of Docker Desktop, Colima or similar on macOS and Windows.</summary>
    public string? SocketPath { get; set; }

    /// <summary>Gets or sets how long the watchdog waits for this process to connect before it gives up and removes the resources. Defaults to one minute.</summary>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Gets or sets how long the watchdog waits, after this process is gone, before it removes the resources. Defaults to ten seconds.</summary>
    public TimeSpan ReconnectionTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
