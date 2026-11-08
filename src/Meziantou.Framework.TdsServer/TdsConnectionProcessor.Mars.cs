using System.Net;
using Meziantou.Framework.Tds.Handler;
using Meziantou.Framework.Tds.Protocol;

namespace Meziantou.Framework.Tds;

internal sealed partial class TdsConnectionProcessor
{
    /// <summary>
    /// Serves a MARS connection: once logged in, the client multiplexes sessions over the connection with SMP, and each
    /// session runs its requests independently of the others.
    /// </summary>
    private async Task ProcessMarsSessionsAsync(Stream input, Stream output, EndPoint remoteEndPoint, TdsAuthenticationResult authenticationResult, CancellationToken cancellationToken)
    {
        using var connection = new TdsSmpConnection(
            input,
            output,
            _logger,
            (sessionStream, sessionCancellationToken) => ProcessRequestsAsync(sessionStream, new TdsPacketWriter(sessionStream, _options.PacketSize), remoteEndPoint, authenticationResult, sessionCancellationToken));

        await connection.RunAsync(cancellationToken).ConfigureAwait(false);
    }
}
