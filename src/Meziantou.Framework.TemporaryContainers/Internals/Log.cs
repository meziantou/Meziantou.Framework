using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.TemporaryContainers.Internals;

internal static partial class Log
{
    // The reason of the failure is deliberately left out of both messages: it comes from the daemon or from the
    // standard error of a CLI, and a registry that echoes back a rejected credential would end up in the log.
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Pulling the image '{Image}' failed on attempt {Attempt}. Retrying in {Delay}.")]
    public static partial void ImagePullRetry(ILogger logger, string image, int attempt, TimeSpan delay);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Looking up the image '{Image}' failed on attempt {Attempt}. Retrying in {Delay}.")]
    public static partial void ImageLookupRetry(ILogger logger, string image, int attempt, TimeSpan delay);

    // The identifiers leave room for the messages other changes add after the first two.
    [LoggerMessage(EventId = 20, Level = LogLevel.Debug, Message = "Using the Docker Engine API at '{Endpoint}' ({Source}).")]
    private static partial void DockerApiEndpointSelectedCore(ILogger logger, string endpoint, string source);

    [LoggerMessage(EventId = 21, Level = LogLevel.Debug, Message = "The Auto runtime uses '{Runtime}': the Docker Engine API is not available. {Reason}")]
    private static partial void DockerApiSkippedCore(ILogger logger, string runtime, string reason);

    // These diagnostics are written while a container is created: a logger backed by a test output helper throws once
    // its test completes, and a diagnostic must not fail the creation.
    public static void DockerApiEndpointSelected(ILogger logger, string endpoint, string source)
    {
        try
        {
            DockerApiEndpointSelectedCore(logger, endpoint, source);
        }
        catch
        {
        }
    }

    public static void DockerApiSkipped(ILogger logger, string runtime, string reason)
    {
        try
        {
            DockerApiSkippedCore(logger, runtime, reason);
        }
        catch
        {
        }
    }

    public static Action<Exception, int, TimeSpan>? CreateImagePullRetryCallback(ILogger? logger, string image)
    {
        if (logger is not { } imageLogger)
            return null;

        return (_, attempt, delay) => ImagePullRetry(imageLogger, image, attempt, delay);
    }

    public static Action<Exception, int, TimeSpan>? CreateImageLookupRetryCallback(ILogger? logger, string image)
    {
        if (logger is not { } imageLogger)
            return null;

        return (_, attempt, delay) => ImageLookupRetry(imageLogger, image, attempt, delay);
    }
}
