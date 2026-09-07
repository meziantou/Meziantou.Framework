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
