using Microsoft.Win32.SafeHandles;

namespace Meziantou.Framework;

/// <summary>
/// Represents a process handle used by <see cref="ProcessWrapper"/>.
/// </summary>
public interface IProcessHandle : IDisposable
{
    /// <summary>Starts the process.</summary>
    /// <returns><see langword="true" /> if the process starts successfully; otherwise, <see langword="false" />.</returns>
    bool Start();

    /// <summary>Gets the process identifier.</summary>
    int Id { get; }

    /// <summary>Gets a value indicating whether the process has exited.</summary>
    bool HasExited { get; }

    /// <summary>Gets the process exit code.</summary>
    int ExitCode { get; }

    /// <summary>Gets the process standard input stream.</summary>
    Stream InputStream { get; }

    /// <summary>Gets the process standard output stream.</summary>
    Stream OutputStream { get; }

    /// <summary>Gets the process standard error stream.</summary>
    Stream ErrorStream { get; }

    /// <summary>
    /// Gets the process safe handle, or <see langword="null" /> if it is not available.
    /// </summary>
    SafeProcessHandle? SafeProcessHandle { get; }

    /// <summary>Asynchronously waits for the process to exit.</summary>
    /// <param name="cancellationToken">A token to cancel waiting.</param>
    Task WaitForExitAsync(CancellationToken cancellationToken);

    /// <summary>Kills the process.</summary>
    /// <param name="entireProcessTree"><see langword="true" /> to kill the entire process tree.</param>
    void Kill(bool entireProcessTree = true);
}
