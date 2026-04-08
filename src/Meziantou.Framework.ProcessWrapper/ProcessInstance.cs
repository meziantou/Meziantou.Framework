using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Win32.SafeHandles;

namespace Meziantou.Framework;

/// <summary>Represents a running process. Dispose to kill the process if still running. Await to wait for exit.</summary>
public class ProcessInstance : IDisposable
{
    private readonly Process _process;
    private readonly Task _inputTask;
    private readonly CancellationTokenRegistration _cancellationRegistration;
    private readonly ProcessValidationMode _validationMode;
    private readonly CancellationToken _cancellationToken;
    private readonly Func<bool> _hasStandardErrorOutput;
    private Task<int>? _waitTask;
    private bool _disposed;

    internal ProcessInstance(Process process, Task inputTask, CancellationTokenRegistration cancellationRegistration, ProcessValidationMode validationMode, Func<bool> hasStandardErrorOutput, CancellationToken cancellationToken)
    {
        _process = process;
        _inputTask = inputTask;
        _cancellationRegistration = cancellationRegistration;
        _validationMode = validationMode;
        _cancellationToken = cancellationToken;
        _hasStandardErrorOutput = hasStandardErrorOutput;

        ProcessId = process.Id;
        SafeProcessHandle = process.SafeHandle;
        StartTime = DateTimeOffset.UtcNow;
    }

    /// <summary>Gets the process ID.</summary>
    public int ProcessId { get; }

    /// <summary>Gets the safe handle to the process.</summary>
    public SafeProcessHandle SafeProcessHandle { get; }

    /// <summary>Gets the time the process was started.</summary>
    public DateTimeOffset StartTime { get; }

    /// <summary>Gets the time the process exited. Available after awaiting.</summary>
    public DateTimeOffset EndTime { get; private set; }

    /// <summary>Gets the duration the process ran. Available after awaiting.</summary>
    public TimeSpan Duration => EndTime - StartTime;

    /// <summary>Gets an awaiter that waits for the process to exit and returns the exit code.</summary>
    public TaskAwaiter<int> GetAwaiter() => WaitForExitCoreAsync().GetAwaiter();

    private Task<int> WaitForExitCoreAsync()
    {
        if (_waitTask is null)
        {
            _waitTask = WaitForExitImplAsync();
        }

        return _waitTask;
    }

    private async Task<int> WaitForExitImplAsync()
    {
        try
        {
            await _process.WaitForExitAsync(_cancellationToken).ConfigureAwait(false);
            await _inputTask.ConfigureAwait(false);
        }
        finally
        {
            EndTime = DateTimeOffset.UtcNow;
            _cancellationRegistration.Dispose();
        }

        _cancellationToken.ThrowIfCancellationRequested();

        var exitCode = _process.ExitCode;
        if ((_validationMode & ProcessValidationMode.FailIfNonZeroExitCode) == ProcessValidationMode.FailIfNonZeroExitCode && exitCode != 0)
        {
            throw new ProcessExecutionException(exitCode);
        }

        if ((_validationMode & ProcessValidationMode.FailIfStdError) == ProcessValidationMode.FailIfStdError && _hasStandardErrorOutput())
        {
            throw new ProcessExecutionException("Process wrote to standard error.");
        }

        return exitCode;
    }

    /// <summary>Disposes the process instance. Kills the process if still running.</summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Disposes managed resources.</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            _cancellationRegistration.Dispose();

            if (!_process.HasExited)
            {
                KillProcess(_process);
            }

            _process.Dispose();
        }

        _disposed = true;
    }

    internal static void KillProcess(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            try
            {
                process.Kill();
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}
