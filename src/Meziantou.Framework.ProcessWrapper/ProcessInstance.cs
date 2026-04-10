using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using Microsoft.Win32.SafeHandles;

namespace Meziantou.Framework;

/// <summary>
/// Represents a running process.
/// Resources are released automatically when the process exits.
/// Await to wait for exit and get a <see cref="ProcessResult"/>.
/// </summary>
public class ProcessInstance
{
    private Process? _process;
    private readonly Task _inputStreamTask;
    private readonly CancellationTokenRegistration _cancellationRegistration;
    private readonly ProcessValidationMode _validationMode;
    private readonly CancellationToken _cancellationToken;
    private readonly Func<bool> _hasStandardErrorOutput;
    private readonly Task<ProcessCompletion> _processCompletionTask;
    private protected readonly Lock WaitTaskLock = new();
    private Task<ProcessResult>? _waitTask;

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
        _processCompletionTask = WaitForProcessExitAsync(process);
    }

    /// <summary>Gets the process ID.</summary>
    public int ProcessId { get; }

    /// <summary>Gets the time the process was started.</summary>
    public DateTimeOffset StartDate { get; }

    /// <summary>Gets the handle to the process. Returns <see langword="null"/> once the process has exited and resources were released.</summary>
    public SafeProcessHandle? UnsafeGetProcessHandle() => _process?.SafeHandle;

    /// <summary>Gets an awaiter that waits for the process to exit and returns the process result.</summary>
    public TaskAwaiter<ProcessResult> GetAwaiter() => GetAwaiterTask().GetAwaiter();

    /// <summary>Kills the process.</summary>
    public void Kill(bool entireProcessTree = true)
    {
        var process = _process;
        if (process is null)
            return;

        try
        {
            process.Kill(entireProcessTree);
        }
        catch (InvalidOperationException) when (entireProcessTree)
        {
            try
            {
                process.Kill();
            }
            catch (InvalidOperationException)
            {
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private protected Task<ProcessResult> GetAwaiterTask()
    {
        if (_waitTask is not null)
            return _waitTask;

        lock (WaitTaskLock)
        {
            _waitTask ??= WaitForExitImplAsync();
        }

        return _waitTask;

        async Task<ProcessResult> WaitForExitImplAsync()
        {
            var processCompletion = await _processCompletionTask.ConfigureAwait(false);
            if (processCompletion.InputStreamException is not null)
            {
                ExceptionDispatchInfo.Capture(processCompletion.InputStreamException).Throw();
            }

            _cancellationToken.ThrowIfCancellationRequested();

            if ((_validationMode & ProcessValidationMode.FailIfNonZeroExitCode) == ProcessValidationMode.FailIfNonZeroExitCode && processCompletion.ExitCode != 0)
            {
                throw new ProcessExecutionException(processCompletion.ExitCode);
            }

            if ((_validationMode & ProcessValidationMode.FailIfStdError) == ProcessValidationMode.FailIfStdError && _hasStandardErrorOutput())
            {
                throw new ProcessExecutionException("Process wrote to standard error.");
            }

            return CreateProcessResult(processCompletion.ExitCode, processCompletion.ExitDate);
        }
    }

    // Wait for process exit and dispose all resources (do not wait for user to await the instance)
    private async Task<ProcessCompletion> WaitForProcessExitAsync(Process process)
    {
        Exception? inputStreamException = null;
        var exitCode = default(int);
        var exitDate = default(DateTimeOffset);

        try
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);

            try
            {
                await _inputStreamTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                inputStreamException = ex;
            }

            exitCode = process.ExitCode;
            exitDate = DateTimeOffset.UtcNow;
        }
        finally
        {
            _cancellationRegistration.Dispose();
            process.Dispose();

            if (ReferenceEquals(_process, process))
            {
                _process = null;
            }
        }

        return new ProcessCompletion(exitCode, exitDate, inputStreamException);
    }

    private protected virtual ProcessResult CreateProcessResult(int exitCode, DateTimeOffset exitDate)
    {
        return new ProcessResult(processId: ProcessId, exitCode: exitCode, startDate: StartDate, exitDate: exitDate);
    }

    private sealed class ProcessCompletion
    {
        public ProcessCompletion(int exitCode, DateTimeOffset exitDate, Exception? inputStreamException)
        {
            ExitCode = exitCode;
            ExitDate = exitDate;
            InputStreamException = inputStreamException;
        }

        public int ExitCode { get; }

        public DateTimeOffset ExitDate { get; }

        public Exception? InputStreamException { get; }
    }
}
