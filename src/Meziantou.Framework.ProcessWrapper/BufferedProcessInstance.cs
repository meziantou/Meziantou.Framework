using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework;

/// <summary>A running process instance that buffers all output and returns it when awaited.</summary>
public sealed class BufferedProcessInstance : ProcessInstance
{
    private readonly ProcessOutputCollection _output;
    private Task<BufferedProcessResult>? _waitTask;

    internal BufferedProcessInstance(Process process, Task inputTask, CancellationTokenRegistration cancellationRegistration, ProcessValidationMode validationMode, ProcessOutputCollection output, Func<bool> hasStandardErrorOutput, CancellationToken cancellationToken)
        : base(process, inputTask, cancellationRegistration, validationMode, hasStandardErrorOutput, cancellationToken)
    {
        _output = output;
    }

    public new TaskAwaiter<BufferedProcessResult> GetAwaiter() => WaitForExitBufferedCoreAsync().GetAwaiter();

    private Task<BufferedProcessResult> WaitForExitBufferedCoreAsync()
    {
        if (_waitTask is null)
        {
            _waitTask = WaitForExitBufferedImplAsync();
        }

        return _waitTask;
    }

    private async Task<BufferedProcessResult> WaitForExitBufferedImplAsync()
    {
        var result = await WaitForExitCoreAsync().ConfigureAwait(false);
        return (BufferedProcessResult)result;
    }

    private protected override ProcessResult CreateProcessResult(int exitCode, DateTimeOffset exitDate)
    {
        return new BufferedProcessResult(processId: ProcessId, exitCode: exitCode, startDate: StartDate, exitDate: exitDate, output: _output);
    }
}
