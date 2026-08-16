namespace Meziantou.Framework.TemporaryContainers.Internals;

internal sealed class ContainerCli
{
    private const int MaxReportedOutputLength = 4000;

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
            .WithValidation(ProcessValidationMode.None);

        if (input is not null)
            wrapper = wrapper.WithInputStream(input);

        var result = await wrapper.ExecuteBufferedAsync(cancellationToken).ConfigureAwait(false);

        var standardOutput = string.Join('\n', result.Output.StandardOutput.Select(o => o.Text));
        var standardError = string.Join('\n', result.Output.StandardError.Select(o => o.Text));

        if (!allowNonZero && !result.ExitCode.IsSuccess)
            throw CreateExecutionException(args, result.ExitCode, standardOutput, standardError);

        return new CliResult(result.ExitCode.Value, standardOutput, standardError);
    }

    public async Task RunToStreamAsync(IReadOnlyList<string> args, Stream standardOutput, CancellationToken cancellationToken)
    {
        var standardError = new StringBuilder();
        var result = await ProcessWrapper.Create(_executable)
            .WithArguments(args)
            .WithValidation(ProcessValidationMode.None)
            .WithOutputStream(OutputTarget.ToStream(standardOutput))
            .WithErrorStream(OutputTarget.ToTextDelegate(line =>
            {
                lock (standardError)
                {
                    standardError.AppendLine(line);
                }
            }))
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!result.ExitCode.IsSuccess)
            throw CreateExecutionException(args, result.ExitCode, standardOutput: "", standardError.ToString());
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

    private ProcessExecutionException CreateExecutionException(IReadOnlyList<string> args, ProcessExitCode exitCode, string standardOutput, string standardError)
    {
        // Only the sub-command is reported. The remaining arguments may contain sensitive data such as environment variable values.
        var command = args.Count > 0 ? _executable + " " + args[0] : _executable;
        var message = new StringBuilder();
        message.Append(CultureInfo.InvariantCulture, $"The command '{command}' exited with code {exitCode}.");
        AppendOutput(message, "Standard error", standardError);
        AppendOutput(message, "Standard output", standardOutput);

        return new ProcessExecutionException(exitCode, message.ToString());

        static void AppendOutput(StringBuilder message, string title, string output)
        {
            output = output.Trim();
            if (output.Length is 0)
                return;

            message.Append('\n').Append(title).Append(':').Append('\n');
            if (output.Length > MaxReportedOutputLength)
            {
                // The relevant part of a container runtime output is usually at the end (progress lines come first).
                message.Append("[...]").Append(output.AsSpan(output.Length - MaxReportedOutputLength));
            }
            else
            {
                message.Append(output);
            }
        }
    }
}
