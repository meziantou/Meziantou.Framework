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
    private readonly Task _outputStreamTask;
    private readonly CancellationTokenRegistration _cancellationRegistration;
    private readonly IDisposable? _processLimiter;
    private readonly ProcessValidationMode _validationMode;
    private readonly CancellationToken _cancellationToken;
    private readonly Func<bool> _hasStandardErrorOutput;
    private readonly Activity? _activity;
    private readonly Task<ProcessCompletion> _processCompletionTask;
    private protected readonly Lock WaitTaskLock = new();
    private Task<ProcessResult>? _waitTask;

    internal ProcessInstance(Process process, Task inputStreamTask, Task outputStreamTask, CancellationTokenRegistration cancellationRegistration, IDisposable? processLimiter, ProcessValidationMode validationMode, Func<bool> hasStandardErrorOutput, Activity? activity, CancellationToken cancellationToken)
    {
        _process = process;
        _inputStreamTask = inputStreamTask;
        _outputStreamTask = outputStreamTask;
        _cancellationRegistration = cancellationRegistration;
        _processLimiter = processLimiter;
        _validationMode = validationMode;
        _cancellationToken = cancellationToken;
        _hasStandardErrorOutput = hasStandardErrorOutput;
        _activity = activity;

        ProcessId = process.Id;
        StartDate = DateTimeOffset.UtcNow;
        _processCompletionTask = WaitForProcessExitAsync(process);
    }

    /// <summary>Gets the process ID.</summary>
    public int ProcessId { get; }

    /// <summary>Gets the time the process was started.</summary>
    public DateTimeOffset StartDate { get; }

    /// <summary>Gets the handle to the process. Returns <see langword="null"/> once the process has exited and resources were released.</summary>
    public SafeProcessHandle? UnsafeGetProcessHandle()
    {
        var process = _process;
        if (process is null)
            return null;

        try
        {
            return process.SafeHandle;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Gets an awaiter that waits for the process to exit and returns the process result.</summary>
    public TaskAwaiter<ProcessResult> GetAwaiter() => GetAwaiterTask().GetAwaiter();

    /// <summary>Configures whether to marshal continuations back to the captured context.</summary>
    public ConfiguredTaskAwaitable<ProcessResult> ConfigureAwait(bool continueOnCapturedContext) => GetAwaiterTask().ConfigureAwait(continueOnCapturedContext);

    /// <summary>Configures how awaits on this instance are performed.</summary>
    public ConfiguredTaskAwaitable<ProcessResult> ConfigureAwait(ConfigureAwaitOptions options) => GetAwaiterTask().ConfigureAwait(options);

    /// <summary>Kills the process.</summary>
    public void Kill(bool entireProcessTree = true)
    {
        var process = _process;
        if (process is null)
            return;

        KillProcess(process, entireProcessTree);
    }

    internal static void KillProcess(Process process, bool entireProcessTree)
    {
        try
        {
            process.Kill(entireProcessTree);
        }
        catch (AggregateException) when (entireProcessTree)
        {
            try
            {
                process.Kill();
            }
            catch (InvalidOperationException)
            {
            }
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

            if (processCompletion.OutputStreamException is not null)
            {
                ExceptionDispatchInfo.Capture(processCompletion.OutputStreamException).Throw();
            }

            _cancellationToken.ThrowIfCancellationRequested();

            var validationException = GetValidationException(processCompletion.ExitCode);
            if (validationException is not null)
            {
                throw validationException;
            }

            return CreateProcessResult(processCompletion.ExitCode, processCompletion.ExitDate);
        }
    }

    private ProcessExecutionException? GetValidationException(ProcessExitCode exitCode)
    {
        if ((_validationMode & ProcessValidationMode.FailIfNonZeroExitCode) == ProcessValidationMode.FailIfNonZeroExitCode && !exitCode.IsSuccess)
        {
            return new ProcessExecutionException(exitCode);
        }

        if ((_validationMode & ProcessValidationMode.FailIfStdError) == ProcessValidationMode.FailIfStdError && _hasStandardErrorOutput())
        {
            return new ProcessExecutionException("Process wrote to standard error.");
        }

        return null;
    }

    // Wait for process exit and dispose all resources (do not wait for user to await the instance)
    private async Task<ProcessCompletion> WaitForProcessExitAsync(Process process)
    {
        Exception? inputStreamException = null;
        Exception? outputStreamException = null;
        var exitCode = default(ProcessExitCode);
        var exitDate = default(DateTimeOffset);
        var processExited = false;

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

            try
            {
                await _outputStreamTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                outputStreamException = ex;
            }

            exitCode = new ProcessExitCode(process.ExitCode);
            exitDate = DateTimeOffset.UtcNow;
            processExited = true;
        }
        finally
        {
            _cancellationRegistration.Dispose();
            process.Dispose();
            _processLimiter?.Dispose();

            if (ReferenceEquals(_process, process))
            {
                _process = null;
            }

            var activity = _activity;
            if (activity is not null)
            {
                if (processExited)
                {
                    activity.SetTag("process.exit.code", (int)exitCode);

                    if (!_cancellationToken.IsCancellationRequested)
                    {
                        var validationException = GetValidationException(exitCode);
                        if (validationException is not null)
                        {
                            activity.SetStatus(ActivityStatusCode.Error, validationException.Message);
                        }
                    }
                }

                activity.Stop();
                activity.Dispose();
            }
        }

        return new ProcessCompletion(exitCode, exitDate, inputStreamException, outputStreamException);
    }

    private protected virtual ProcessResult CreateProcessResult(ProcessExitCode exitCode, DateTimeOffset exitDate)
    {
        return new ProcessResult(processId: ProcessId, exitCode: exitCode, startDate: StartDate, exitDate: exitDate);
    }

    private sealed class ProcessCompletion
    {
        public ProcessCompletion(ProcessExitCode exitCode, DateTimeOffset exitDate, Exception? inputStreamException, Exception? outputStreamException)
        {
            ExitCode = exitCode;
            ExitDate = exitDate;
            InputStreamException = inputStreamException;
            OutputStreamException = outputStreamException;
        }

        public ProcessExitCode ExitCode { get; }

        public DateTimeOffset ExitDate { get; }

        public Exception? InputStreamException { get; }

        public Exception? OutputStreamException { get; }
    }
}
