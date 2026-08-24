using System.Text;

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
        // The validation is not delegated to ProcessWrapper: it throws before the output is read, which leaves the
        // caller with an exit code and no indication of what the runtime actually complained about.
        var wrapper = ProcessWrapper.Create(_executable)
            .WithArguments(args)
            .WithValidation(ProcessValidationMode.None);

        if (input is not null)
            wrapper = wrapper.WithInputStream(input);

        var result = await wrapper.ExecuteBufferedAsync(cancellationToken).ConfigureAwait(false);

        var standardOutput = string.Join('\n', result.Output.StandardOutput.Select(o => o.Text));
        var standardError = string.Join('\n', result.Output.StandardError.Select(o => o.Text));
        var exitCode = result.ExitCode.Value;

        if (!allowNonZero && exitCode != 0)
            throw CreateFailure(args, exitCode, standardOutput, standardError);

        return new CliResult(exitCode, standardOutput, standardError);
    }

    public async Task RunToStreamAsync(IReadOnlyList<string> args, Stream standardOutput, CancellationToken cancellationToken)
    {
        // The target synchronizes its own writes and flushes the decoder when the process completes, and the process
        // wrapper awaits the output pumps before returning, so the text is complete once the call below returns.
        var standardError = new StringBuilder();
        var result = await ProcessWrapper.Create(_executable)
            .WithArguments(args)
            .WithValidation(ProcessValidationMode.None)
            .WithOutputStream(OutputTarget.ToStream(standardOutput))
            .WithErrorStream(OutputTarget.ToStringBuilder(standardError))
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);

        var exitCode = result.ExitCode.Value;
        if (exitCode != 0)
            throw CreateFailure(args, exitCode, standardOutput: "", standardError.ToString());
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

    private ContainerRuntimeException CreateFailure(IReadOnlyList<string> args, int exitCode, string standardOutput, string standardError)
    {
        var command = FormatCommand(_executable, args);

        var message = new StringBuilder();
        message.Append(CultureInfo.InvariantCulture, $"The '{Runtime}' container runtime command failed with exit code {exitCode}.");
        message.Append("\nCommand: ").Append(command);

        if (!string.IsNullOrWhiteSpace(standardError))
            message.Append("\nStandard error:\n").Append(standardError.TrimEnd());

        // The runtimes do not consistently use the standard error, so the standard output is the only clue left when it is empty.
        if (!string.IsNullOrWhiteSpace(standardOutput))
            message.Append("\nStandard output:\n").Append(standardOutput.TrimEnd());

        return new ContainerRuntimeException(message.ToString(), Runtime, command, exitCode, standardOutput, standardError);
    }

    internal static string FormatCommand(string executable, IReadOnlyList<string> args)
    {
        var result = new StringBuilder();
        result.Append(CommandLineBuilder.WindowsQuotedArgument(executable));

        var redactNextValue = false;
        foreach (var arg in args)
        {
            result.Append(' ').Append(CommandLineBuilder.WindowsQuotedArgument(redactNextValue ? Redact(arg) : arg));

            // Environment variables are the one place where the caller routinely passes secrets to the runtime.
            redactNextValue = arg is "--env" or "-e";
        }

        return result.ToString();

        static string Redact(string value)
        {
            var separator = value.IndexOf('=', StringComparison.Ordinal);
            return separator < 0 ? "***" : string.Concat(value.AsSpan(0, separator + 1), "***");
        }
    }
}
