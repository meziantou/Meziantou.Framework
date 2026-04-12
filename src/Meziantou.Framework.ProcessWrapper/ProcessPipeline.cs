using System.Collections.Immutable;

namespace Meziantou.Framework;

/// <summary>
/// Represents an ordered set of commands connected through standard streams.
/// </summary>
public sealed class ProcessPipeline
{
    private readonly ImmutableArray<ProcessWrapper> _commands;

    private ProcessPipeline(ImmutableArray<ProcessWrapper> commands)
    {
        if (commands.Length < 2)
            throw new ArgumentException("A pipeline must contain at least 2 commands.", nameof(commands));

        _commands = commands;
    }

    internal static ProcessPipeline Create(ProcessWrapper first, ProcessWrapper second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        return new ProcessPipeline([first, second]);
    }

    /// <summary>
    /// Appends a command to the pipeline.
    /// </summary>
    public static ProcessPipeline operator |(ProcessPipeline left, ProcessWrapper right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return left.Append(right);
    }

    /// <summary>
    /// Prepends a command to the pipeline.
    /// </summary>
    public static ProcessPipeline operator |(ProcessWrapper left, ProcessPipeline right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return right.Prepend(left);
    }

    /// <summary>
    /// Executes the pipeline and returns the result of the last command.
    /// </summary>
    public Task<ProcessResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var execution = StartStages(bufferLastCommand: false, cancellationToken);
        return WaitForPipelineAsync(execution);
    }

    /// <summary>
    /// Executes the pipeline with buffering enabled for the last command.
    /// </summary>
    public Task<BufferedProcessResult> ExecuteBufferedAsync(CancellationToken cancellationToken = default)
    {
        var execution = StartStages(bufferLastCommand: true, cancellationToken);
        return WaitForBufferedPipelineAsync(execution);
    }

    private ProcessPipeline Append(ProcessWrapper command)
    {
        return new ProcessPipeline(_commands.Add(command));
    }

    private ProcessPipeline Prepend(ProcessWrapper command)
    {
        return new ProcessPipeline(_commands.Insert(0, command));
    }

    private PipelineExecution StartStages(bool bufferLastCommand, CancellationToken cancellationToken)
    {
        var commands = CloneCommands();
        var pipes = CreatePipes(commands.Length);
        WireCommands(commands, pipes);

        var instances = new ProcessInstance?[commands.Length];
        var stageTasks = new Task<ProcessResult>[commands.Length];
        var lastBufferedInstance = default(BufferedProcessInstance);

        try
        {
            for (var index = commands.Length - 1; index >= 0; index--)
            {
                if (bufferLastCommand && index == commands.Length - 1)
                {
                    var bufferedInstance = commands[index].ExecuteBufferedAsync(cancellationToken);
                    instances[index] = bufferedInstance;
                    stageTasks[index] = WaitForStageAsync(bufferedInstance);
                    lastBufferedInstance = bufferedInstance;
                }
                else
                {
                    var instance = commands[index].ExecuteAsync(cancellationToken);
                    instances[index] = instance;
                    stageTasks[index] = WaitForStageAsync(instance);
                }
            }
        }
        catch
        {
            KillStartedProcesses(instances);
            throw;
        }

        return new PipelineExecution(stageTasks, instances[^1]!, lastBufferedInstance);
    }

    private ProcessWrapper[] CloneCommands()
    {
        var commands = new ProcessWrapper[_commands.Length];
        for (var index = 0; index < _commands.Length; index++)
        {
            commands[index] = _commands[index].Clone();
        }

        return commands;
    }

    private static ProcessPipe[] CreatePipes(int commandCount)
    {
        var pipes = new ProcessPipe[commandCount - 1];
        for (var index = 0; index < pipes.Length; index++)
        {
            pipes[index] = new ProcessPipe();
        }

        return pipes;
    }

    private static void WireCommands(ProcessWrapper[] commands, ProcessPipe[] pipes)
    {
        for (var index = 1; index < commands.Length; index++)
        {
            if (commands[index].HasInputSource)
                throw new InvalidOperationException("Pipeline input cannot be applied because one command already has an input stream.");

            commands[index].WithInputStream(pipes[index - 1]);
        }

        for (var index = 0; index < pipes.Length; index++)
        {
            commands[index].AddOutputStream(pipes[index]);
        }
    }

    private static void KillStartedProcesses(ProcessInstance?[] instances)
    {
        for (var index = instances.Length - 1; index >= 0; index--)
        {
            instances[index]?.Kill();
        }
    }

    private static async Task<ProcessResult> WaitForPipelineAsync(PipelineExecution execution)
    {
        await Task.WhenAll(execution.StageTasks).ConfigureAwait(false);
        return await execution.LastInstance.ConfigureAwait(false);
    }

    private static async Task<BufferedProcessResult> WaitForBufferedPipelineAsync(PipelineExecution execution)
    {
        ArgumentNullException.ThrowIfNull(execution.LastBufferedInstance);

        await Task.WhenAll(execution.StageTasks).ConfigureAwait(false);
        return await execution.LastBufferedInstance.ConfigureAwait(false);
    }

    private static async Task<ProcessResult> WaitForStageAsync(ProcessInstance instance)
    {
        return await instance.ConfigureAwait(false);
    }

    private sealed class PipelineExecution(Task<ProcessResult>[] stageTasks, ProcessInstance lastInstance, BufferedProcessInstance? lastBufferedInstance)
    {
        public Task<ProcessResult>[] StageTasks { get; } = stageTasks;

        public ProcessInstance LastInstance { get; } = lastInstance;

        public BufferedProcessInstance? LastBufferedInstance { get; } = lastBufferedInstance;
    }
}
