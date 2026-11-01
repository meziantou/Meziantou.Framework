using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Meziantou.Framework.TemporaryContainers.Strategies;

namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Provides factory methods for the built-in <see cref="IWaitStrategy"/> implementations.</summary>
public static class Wait
{
    /// <summary>Waits until a TCP connection can be established on the host port mapped to <paramref name="containerPort"/>.</summary>
    /// <param name="containerPort">The container port to probe.</param>
    /// <returns>A wait strategy.</returns>
    public static IWaitStrategy ForPort(int containerPort)
    {
        return new PortWaitStrategy(containerPort);
    }

    /// <summary>Waits until a log line contains the specified text.</summary>
    /// <param name="substring">The text to look for.</param>
    /// <param name="occurrences">The number of matching lines to wait for.</param>
    /// <returns>A wait strategy.</returns>
    public static IWaitStrategy ForLogMessage(string substring, int occurrences = 1)
    {
        ArgumentNullException.ThrowIfNull(substring);
        return new LogMessageWaitStrategy(new Regex(Regex.Escape(substring), RegexOptions.None, TimeSpan.FromSeconds(1)), occurrences, substring);
    }

    /// <summary>Waits until a log line matches the specified pattern.</summary>
    /// <param name="pattern">The pattern to match against each log line.</param>
    /// <param name="occurrences">The number of matching lines to wait for.</param>
    /// <returns>A wait strategy.</returns>
    public static IWaitStrategy ForLogMessage(Regex pattern, int occurrences = 1)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return new LogMessageWaitStrategy(pattern, occurrences);
    }

    /// <summary>Waits for a fixed amount of time.</summary>
    /// <param name="delay">The delay to wait.</param>
    /// <returns>A wait strategy.</returns>
    public static IWaitStrategy ForDelay(TimeSpan delay)
    {
        return new DelayWaitStrategy(delay);
    }

    /// <summary>Waits until a command run inside the container exits with code 0. The command is run again after each failure.</summary>
    /// <param name="command">The command and its arguments. The first element is the executable.</param>
    /// <returns>A wait strategy.</returns>
    public static IWaitStrategy ForCommand(params string[] command)
    {
        return ForCommand(command, configure: null);
    }

    /// <summary>Waits until a command run inside the container exits with code 0. The command is run again after each failure.</summary>
    /// <param name="command">The command and its arguments. The first element is the executable.</param>
    /// <param name="configure">Configures the delay between two runs of the command.</param>
    /// <returns>A wait strategy.</returns>
    public static IWaitStrategy ForCommand(IEnumerable<string> command, Action<PollingWaitStrategyOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(command);

        string[] tokens = [.. command];
        if (tokens.Length == 0)
            throw new ArgumentException("The command must not be empty.", nameof(command));

        if (Array.IndexOf(tokens, null) >= 0)
            throw new ArgumentException("The command must not contain null elements.", nameof(command));

        var options = new PollingWaitStrategyOptions();
        configure?.Invoke(options);
        options.Validate(nameof(configure));
        return new CommandWaitStrategy(tokens, options.PollInterval);
    }

    /// <summary>Waits until an HTTP request to the host port mapped to <paramref name="containerPort"/> gets an expected response, by default a 2xx status code.</summary>
    /// <param name="containerPort">The container port to send the request to.</param>
    /// <param name="path">The path, and optionally the query, of the request.</param>
    /// <param name="configure">Configures the request and the response that means the container is ready.</param>
    /// <returns>A wait strategy.</returns>
    public static IWaitStrategy ForHttp(int containerPort, string path = "/", Action<HttpWaitStrategyOptions>? configure = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(containerPort, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(containerPort, 65535);
        ArgumentNullException.ThrowIfNull(path);

        var options = new HttpWaitStrategyOptions();
        configure?.Invoke(options);
        options.Validate(nameof(configure));
        return new HttpWaitStrategy(containerPort, path, options);
    }

    /// <summary>Waits until the container runtime reports the health check of the container (the <c>HEALTHCHECK</c> instruction of its image) as healthy. The wait fails at once when the container has no health check or becomes unhealthy.</summary>
    /// <param name="configure">Configures the delay between two inspections of the container.</param>
    /// <returns>A wait strategy.</returns>
    public static IWaitStrategy ForHealthcheck(Action<PollingWaitStrategyOptions>? configure = null)
    {
        var options = new PollingWaitStrategyOptions();
        configure?.Invoke(options);
        options.Validate(nameof(configure));
        return new HealthcheckWaitStrategy(options.PollInterval);
    }

    /// <summary>Runs the strategies concurrently and waits until all of them complete. The first failure stops the others and fails the wait.</summary>
    /// <param name="strategies">The strategies to run.</param>
    /// <returns>A wait strategy.</returns>
    public static IWaitStrategy ForAll(params IWaitStrategy[] strategies)
    {
        return new AllWaitStrategy(CopyStrategies(strategies));
    }

    /// <summary>Runs the strategies concurrently and waits until one of them completes, then stops the others. The wait fails only when all of them fail.</summary>
    /// <param name="strategies">The strategies to run.</param>
    /// <returns>A wait strategy.</returns>
    public static IWaitStrategy ForAny(params IWaitStrategy[] strategies)
    {
        var copy = CopyStrategies(strategies);
        if (copy.Length == 0)
            throw new ArgumentException("At least one strategy is required.", nameof(strategies));

        return new AnyWaitStrategy(copy);
    }

    private static IWaitStrategy[] CopyStrategies(IWaitStrategy[] strategies, [CallerArgumentExpression(nameof(strategies))] string? paramName = null)
    {
        ArgumentNullException.ThrowIfNull(strategies, paramName);

        IWaitStrategy[] copy = [.. strategies];
        if (Array.IndexOf(copy, null) >= 0)
            throw new ArgumentException("The strategies must not contain null elements.", paramName);

        return copy;
    }
}
