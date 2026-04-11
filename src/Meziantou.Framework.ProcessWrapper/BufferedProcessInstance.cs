using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework;

/// <summary>A running process instance that buffers all output and returns it when awaited.</summary>
public sealed class BufferedProcessInstance : ProcessInstance
{
    private readonly ProcessOutputCollection _output;
    private Task<BufferedProcessResult>? _waitTask;

    internal BufferedProcessInstance(Process process, Task inputTask, Task outputTask, CancellationTokenRegistration cancellationRegistration, IDisposable? processLimiter, ProcessValidationMode validationMode, ProcessOutputCollection output, Func<bool> hasStandardErrorOutput, CancellationToken cancellationToken)
        : base(process, inputTask, outputTask, cancellationRegistration, processLimiter, validationMode, hasStandardErrorOutput, cancellationToken)
    {
        _output = output;
    }

    public new TaskAwaiter<BufferedProcessResult> GetAwaiter() => WaitForExitBufferedCoreAsync().GetAwaiter();

    private Task<BufferedProcessResult> WaitForExitBufferedCoreAsync()
    {
        if (_waitTask is not null)
            return _waitTask;

        lock (WaitTaskLock)
        {
            _waitTask ??= WaitForExitBufferedImplAsync();
        }

        return _waitTask;

        async Task<BufferedProcessResult> WaitForExitBufferedImplAsync()
        {
            var result = await GetAwaiterTask().ConfigureAwait(false);
            return (BufferedProcessResult)result;
        }
    }

    private protected override ProcessResult CreateProcessResult(int exitCode, DateTimeOffset exitDate)
    {
        return new BufferedProcessResult(processId: ProcessId, exitCode: exitCode, startDate: StartDate, exitDate: exitDate, output: _output);
    }
}
