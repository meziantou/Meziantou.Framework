using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Logging options for a container.</summary>
public sealed class ContainerLoggingOptions
{
    internal ContainerLoggingOptions()
    {
    }

    internal ContainerLoggingOptions(ContainerLoggingOptions other)
    {
        Logger = other.Logger;
        CaptureStandardOutput = other.CaptureStandardOutput;
        CaptureStandardError = other.CaptureStandardError;
    }

    /// <summary>Gets or sets the logger used to forward container logs while the container is running.</summary>
    public ILogger? Logger { get; set; }

    /// <summary>Gets or sets a value indicating whether standard output is forwarded to <see cref="Logger"/>.</summary>
    public bool CaptureStandardOutput { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether standard error is forwarded to <see cref="Logger"/>.</summary>
    public bool CaptureStandardError { get; set; } = true;
}
