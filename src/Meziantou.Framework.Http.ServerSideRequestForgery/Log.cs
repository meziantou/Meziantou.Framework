using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.Http.ServerSideRequestForgery;

internal static partial class Log
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Rejected request to {requestOrigin}. Scheme '{scheme}' is not allowed.")]
    public static partial void RejectedUnsafeScheme(ILogger logger, string requestOrigin, string scheme);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Rejected request to {requestOrigin}. Connect endpoint host '{endpointHost}' does not match request authority '{requestHost}'.")]
    public static partial void RejectedHostMismatch(ILogger logger, string requestOrigin, string endpointHost, string requestHost);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Rejected request to {requestOrigin}. All resolved IP addresses were unsafe.")]
    public static partial void RejectedAllResolvedAddressesUnsafe(ILogger logger, string requestOrigin);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Rejected request to {requestOrigin}. DNS resolved both safe and unsafe IP addresses while mixed results are disallowed.")]
    public static partial void RejectedMixedResolvedAddresses(ILogger logger, string requestOrigin);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "Rejected request to {requestOrigin}. Resolution strategy selected an address outside the validated safe set.")]
    public static partial void RejectedSelectedAddressNotInSafeSet(ILogger logger, string requestOrigin);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "Rejected request to {requestOrigin}. Resolution strategy failed: {reason}")]
    public static partial void RejectedResolutionStrategyFailure(ILogger logger, string requestOrigin, string reason);

    [LoggerMessage(EventId = 7, Level = LogLevel.Warning, Message = "Rejected request to {requestOrigin}. The connection targets proxy '{proxyHost}', whose tunnelled destination cannot be validated.")]
    public static partial void RejectedProxyConnection(ILogger logger, string requestOrigin, string proxyHost);
}
