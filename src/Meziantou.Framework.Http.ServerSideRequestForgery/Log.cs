using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.Http.ServerSideRequestForgery;

internal static partial class Log
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Rejected request to {requestUri}. Scheme '{scheme}' is not allowed.")]
    public static partial void RejectedUnsafeScheme(ILogger logger, Uri requestUri, string scheme);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Rejected request to {requestUri}. Connect endpoint host '{endpointHost}' does not match request authority '{requestHost}'.")]
    public static partial void RejectedHostMismatch(ILogger logger, Uri requestUri, string endpointHost, string requestHost);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Rejected request to {requestUri}. All resolved IP addresses were unsafe.")]
    public static partial void RejectedAllResolvedAddressesUnsafe(ILogger logger, Uri requestUri);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Rejected request to {requestUri}. DNS resolved both safe and unsafe IP addresses while mixed results are disallowed.")]
    public static partial void RejectedMixedResolvedAddresses(ILogger logger, Uri requestUri);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "Rejected request to {requestUri}. Resolution strategy selected an address outside the validated safe set.")]
    public static partial void RejectedSelectedAddressNotInSafeSet(ILogger logger, Uri requestUri);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "Rejected request to {requestUri}. Resolution strategy failed: {reason}")]
    public static partial void RejectedResolutionStrategyFailure(ILogger logger, Uri requestUri, string reason);
}
