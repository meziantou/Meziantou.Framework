using Meziantou.Framework.TemporaryContainers.Internals;
using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Logging options for a container.</summary>
public sealed class ContainerLoggingOptions
{
    private bool _isReadOnly;

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
    public ILogger? Logger
    {
        get;
        set
        {
            DefinitionReadOnly.ThrowIf(_isReadOnly);
            field = value;
        }
    }

    /// <summary>Gets or sets a value indicating whether standard output is forwarded to <see cref="Logger"/>.</summary>
    public bool CaptureStandardOutput
    {
        get;
        set
        {
            DefinitionReadOnly.ThrowIf(_isReadOnly);
            field = value;
        }
    } = true;

    /// <summary>Gets or sets a value indicating whether standard error is forwarded to <see cref="Logger"/>.</summary>
    public bool CaptureStandardError
    {
        get;
        set
        {
            DefinitionReadOnly.ThrowIf(_isReadOnly);
            field = value;
        }
    } = true;

    internal void MakeReadOnly() => _isReadOnly = true;
}
