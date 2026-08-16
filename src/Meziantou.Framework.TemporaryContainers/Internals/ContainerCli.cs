namespace Meziantou.Framework.TemporaryContainers.Internals;

internal sealed class ContainerCli
{
    private readonly string _executable;

    public ContainerCli(ContainerRuntime runtime, string executable)
    {
        Runtime = runtime;
        _executable = executable;
    }

    public ContainerRuntime Runtime { get; }

    public async Task<CliResult> RunBufferedAsync(IReadOnlyList<string> args, CancellationToken cancellationToken, bool allowNonZero = false, InputSource? input = null)
    {
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
        await ProcessWrapper.Create(_executable)
            .WithArguments(args)
            .WithValidation(ProcessValidationMode.FailIfNonZeroExitCode)
            .WithOutputStream(OutputTarget.ToStream(standardOutput))
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public ProcessInstance ExecuteStreaming(IReadOnlyList<string> args, Action<string> onStandardOutput, Action<string> onStandardError, CancellationToken cancellationToken)
    {
        return ProcessWrapper.Create(_executable)
            .WithArguments(args)
            .WithValidation(ProcessValidationMode.None)
            .WithOutputStream(OutputTarget.ToTextDelegate(onStandardOutput))
            .WithErrorStream(OutputTarget.ToTextDelegate(onStandardError))
            .ExecuteAsync(cancellationToken);
    }

}
