using System.Diagnostics;

namespace Meziantou.Framework;

/// <summary>A process instance that also buffers all output. Inherits from <see cref="ProcessInstance"/>.</summary>
public sealed class BufferedProcessInstance : ProcessInstance
{
    internal BufferedProcessInstance(Process process, Task inputTask, CancellationTokenRegistration cancellationRegistration, ProcessValidationMode validationMode, ProcessOutputCollection output, CancellationToken cancellationToken, Func<bool> hasStandardErrorOutput)
        : base(process, inputTask, cancellationRegistration, validationMode, cancellationToken, hasStandardErrorOutput)
    {
        Output = output;
    }

    /// <summary>Gets the interleaved output from both standard output and standard error streams.</summary>
    public ProcessOutputCollection Output { get; }
}
