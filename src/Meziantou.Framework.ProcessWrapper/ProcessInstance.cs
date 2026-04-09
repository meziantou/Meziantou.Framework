using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Win32.SafeHandles;

namespace Meziantou.Framework;

/// <summary>Represents a running process. Dispose to kill the process if still running. Await to wait for exit.</summary>
public class ProcessInstance : IDisposable
{
    private Process? _process;
    private readonly Task _inputStreamTask;
    private readonly CancellationTokenRegistration _cancellationRegistration;
    private readonly ProcessValidationMode _validationMode;
    private readonly CancellationToken _cancellationToken;
    private readonly Func<bool> _hasStandardErrorOutput;
    private Task<ProcessResult>? _waitTask;
    private ProcessResult? _completedResult;
    private bool _disposed;

    internal ProcessInstance(Process process, Task inputStreamTask, CancellationTokenRegistration cancellationRegistration, ProcessValidationMode validationMode, Func<bool> hasStandardErrorOutput, CancellationToken cancellationToken)
    {
        _process = process;
        _inputStreamTask = inputStreamTask;
        _cancellationRegistration = cancellationRegistration;
        _validationMode = validationMode;
        _cancellationToken = cancellationToken;
        _hasStandardErrorOutput = hasStandardErrorOutput;

        ProcessId = process.Id;
        StartDate = DateTimeOffset.UtcNow;
    }

    /// <summary>Gets the process ID.</summary>
    public int ProcessId { get; }

    /// <summary>Gets the time the process was started.</summary>
    public DateTimeOffset StartDate { get; }

    /// <summary>Gets the handle to the process. If the process is still running, the handle is valid.</summary>
    public SafeProcessHandle? UnsafeGetProcessHandle() => _process?.SafeHandle;

    /// <summary>Gets an awaiter that waits for the process to exit and returns the process result.</summary>
    public TaskAwaiter<ProcessResult> GetAwaiter() => WaitForExitCoreAsync().GetAwaiter();

    private protected Task<ProcessResult> WaitForExitCoreAsync()
    {
        if (_waitTask is null)
        {
            _waitTask = WaitForExitImplAsync();
        }

        return _waitTask;
    }

    private async Task<ProcessResult> WaitForExitImplAsync()
    {
        var process = _process;
        if (process is null)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _completedResult ?? throw new InvalidOperationException("Process is not available.");
        }

        var exitDate = default(DateTimeOffset);

        try
        {
            try
            {
                await process.WaitForExitAsync(_cancellationToken).ConfigureAwait(false);
                await _inputStreamTask.ConfigureAwait(false);
            }
            finally
            {
                exitDate = DateTimeOffset.UtcNow;
                _cancellationRegistration.Dispose();
            }

            _cancellationToken.ThrowIfCancellationRequested();

            var exitCode = process.ExitCode;
            if ((_validationMode & ProcessValidationMode.FailIfNonZeroExitCode) == ProcessValidationMode.FailIfNonZeroExitCode && exitCode != 0)
            {
                throw new ProcessExecutionException(exitCode);
            }

            if ((_validationMode & ProcessValidationMode.FailIfStdError) == ProcessValidationMode.FailIfStdError && _hasStandardErrorOutput())
            {
                throw new ProcessExecutionException("Process wrote to standard error.");
            }

            var result = CreateProcessResult(exitCode, exitDate);
            _completedResult = result;
            return result;
        }
        finally
        {
            process.Dispose();
            _process = null;
        }
    }

    private protected virtual ProcessResult CreateProcessResult(int exitCode, DateTimeOffset exitDate)
    {
        return new ProcessResult(processId: ProcessId, exitCode: exitCode, startDate: StartDate, exitDate: exitDate);
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

            if (_process is not null && !_process.HasExited)
            {
                KillProcess(_process);
            }

            _process?.Dispose();
            _process = null;
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
