using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.TemporaryContainers.Internals;

internal sealed partial class ContainerCli
{
    private readonly string _executable;
    private readonly ILogger? _logger;

    public ContainerCli(ContainerRuntime runtime, string executable, ILogger? logger)
    {
        Runtime = runtime;
        _executable = executable;
        _logger = logger;
    }

    public ContainerRuntime Runtime { get; }

    public async Task<CliResult> RunBufferedAsync(IReadOnlyList<string> args, CancellationToken cancellationToken, bool allowNonZero = false, InputSource? input = null)
    {
        LogExecuting(args);

        var wrapper = ProcessWrapper.Create(_executable)
            .WithArguments(args)
            .WithValidation(allowNonZero ? ProcessValidationMode.None : ProcessValidationMode.FailIfNonZeroExitCode);

        if (input is not null)
            wrapper = wrapper.WithInputStream(input);

        var result = await wrapper.ExecuteBufferedAsync(cancellationToken).ConfigureAwait(false);

        var standardOutput = string.Join('\n', result.Output.StandardOutput.Select(o => o.Text));
        var standardError = string.Join('\n', result.Output.StandardError.Select(o => o.Text));
        return new CliResult(result.ExitCode.Value, standardOutput, standardError);
    }

    public async Task RunToStreamAsync(IReadOnlyList<string> args, Stream standardOutput, CancellationToken cancellationToken)
    {
        LogExecuting(args);

        await ProcessWrapper.Create(_executable)
            .WithArguments(args)
            .WithValidation(ProcessValidationMode.FailIfNonZeroExitCode)
            .WithOutputStream(OutputTarget.ToStream(standardOutput))
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public ProcessInstance ExecuteStreaming(IReadOnlyList<string> args, Action<string> onStandardOutput, Action<string> onStandardError, CancellationToken cancellationToken)
    {
        LogExecuting(args);

        return ProcessWrapper.Create(_executable)
            .WithArguments(args)
            .WithValidation(ProcessValidationMode.None)
            .WithOutputStream(OutputTarget.ToTextDelegate(onStandardOutput))
            .WithErrorStream(OutputTarget.ToTextDelegate(onStandardError))
            .ExecuteAsync(cancellationToken);
    }

    private void LogExecuting(IReadOnlyList<string> args)
    {
        if (_logger is { } logger && logger.IsEnabled(LogLevel.Debug))
            LogExecuting(logger, Runtime.ToString(), string.Join(' ', args));
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Executing {Runtime} {Arguments}")]
    private static partial void LogExecuting(ILogger logger, string runtime, string arguments);
}
