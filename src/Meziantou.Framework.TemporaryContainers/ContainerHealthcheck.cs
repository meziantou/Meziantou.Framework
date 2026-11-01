namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Overrides the healthcheck an image declares (<c>HEALTHCHECK</c>), or disables it.</summary>
/// <remarks>Apple's container runtime has no healthcheck, and rejects a definition that sets one.</remarks>
public sealed class ContainerHealthcheck
{
    private ContainerHealthcheck(string? shellCommand, IReadOnlyList<string>? command)
    {
        ShellCommand = shellCommand;
        Command = command;
    }

    /// <summary>Initializes a new instance of the <see cref="ContainerHealthcheck"/> class that runs a command through the shell of the container (<c>CMD-SHELL</c>).</summary>
    /// <param name="shellCommand">The command, run with <c>/bin/sh -c</c>. The container is healthy when it exits with code 0.</param>
    public ContainerHealthcheck(string shellCommand)
        : this(ValidateShellCommand(shellCommand), command: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ContainerHealthcheck"/> class that runs an executable without a shell (<c>CMD</c>).</summary>
    /// <param name="command">The executable and its arguments. The container is healthy when it exits with code 0.</param>
    /// <remarks>The CLI runtimes only take a shell command, so they run the arguments, quoted, through the shell of the container.</remarks>
    public ContainerHealthcheck(IEnumerable<string> command)
        : this(shellCommand: null, ValidateCommand(command))
    {
    }

    /// <summary>Gets a healthcheck that disables the one the image declares.</summary>
    public static ContainerHealthcheck Disabled { get; } = new(shellCommand: null, command: null);

    /// <summary>Gets the command run through the shell of the container, or <see langword="null"/> when the healthcheck runs <see cref="Command"/> or is disabled.</summary>
    public string? ShellCommand { get; }

    /// <summary>Gets the executable and the arguments the healthcheck runs, or <see langword="null"/> when it runs <see cref="ShellCommand"/> or is disabled.</summary>
    public IReadOnlyList<string>? Command { get; }

    /// <summary>Gets a value indicating whether this healthcheck disables the one the image declares.</summary>
    public bool IsDisabled => ShellCommand is null && Command is null;

    /// <summary>Gets the time between two checks. <see langword="null"/> keeps the default of the runtime.</summary>
    public TimeSpan? Interval { get; init => field = ValidateDuration(value); }

    /// <summary>Gets the time a check may run before it is considered failed. <see langword="null"/> keeps the default of the runtime.</summary>
    public TimeSpan? Timeout { get; init => field = ValidateDuration(value); }

    /// <summary>Gets the time the container is given to start, during which failed checks do not count. <see langword="null"/> keeps the default of the runtime.</summary>
    public TimeSpan? StartPeriod { get; init => field = ValidateDuration(value); }

    /// <summary>Gets the number of consecutive failures that make the container unhealthy. <see langword="null"/> keeps the default of the runtime.</summary>
    public int? Retries
    {
        get;
        init
        {
            if (value is { } retries)
                ArgumentOutOfRangeException.ThrowIfNegative(retries, nameof(value));

            field = value;
        }
    }

    /// <summary>The <c>Test</c> array of the Docker Engine API.</summary>
    internal string[] GetTest()
    {
        if (ShellCommand is { } shellCommand)
            return ["CMD-SHELL", shellCommand];

        if (Command is { } command)
            return ["CMD", .. command];

        return ["NONE"];
    }

    /// <summary>The command the CLI runtimes take, which always runs through the shell.</summary>
    internal string? GetShellCommand()
    {
        if (ShellCommand is { } shellCommand)
            return shellCommand;

        if (Command is { } command)
            return string.Join(' ', command.Select(static argument => "'" + argument.Replace("'", "'\\''", StringComparison.Ordinal) + "'"));

        return null;
    }

    /// <summary>A value that identifies the healthcheck in the configuration hash of a container.</summary>
    internal string GetHashValue()
        => string.Join('\u001f', GetTest()) + "|" + Interval?.Ticks.ToString(CultureInfo.InvariantCulture) + "|" + Timeout?.Ticks.ToString(CultureInfo.InvariantCulture) + "|" + StartPeriod?.Ticks.ToString(CultureInfo.InvariantCulture) + "|" + Retries?.ToString(CultureInfo.InvariantCulture);

    private static string ValidateShellCommand(string shellCommand)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shellCommand);
        return shellCommand;
    }

    private static string[] ValidateCommand(IEnumerable<string> command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var result = command.ToArray();
        if (result.Length == 0)
            throw new ArgumentException("The command must contain at least the executable.", nameof(command));

        foreach (var argument in result)
        {
            if (argument is null)
                throw new ArgumentException("The command cannot contain a null argument.", nameof(command));
        }

        return result;
    }

    private static TimeSpan? ValidateDuration(TimeSpan? value)
    {
        if (value is { } duration)
            ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.FromMilliseconds(1), nameof(value));

        return value;
    }
}
