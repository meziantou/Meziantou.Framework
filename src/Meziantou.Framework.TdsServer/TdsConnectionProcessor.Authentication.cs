using Meziantou.Framework.Tds.Handler;
using Meziantou.Framework.Tds.Protocol;
using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.Tds;

internal sealed partial class TdsConnectionProcessor
{
    /// <summary>The number of SSPI round trips a login may take. NTLM takes two and Kerberos one or two.</summary>
    private const int MaxSspiRounds = 16;

    /// <summary>
    /// Runs the authentication handler, and the SSPI exchange when the handler asks for one. Returns <see langword="null"/>
    /// when the client closes the connection in the middle of the exchange.
    /// </summary>
    private async Task<TdsAuthenticationResult?> AuthenticateAsync(Stream input, TdsPacketWriter writer, TdsAuthenticationContext context, CancellationToken cancellationToken)
    {
        try
        {
            for (var round = 1; ; round++)
            {
                var result = await _authenticationHandler(context, cancellationToken).ConfigureAwait(false);
                if (!result.IsSspiContinuationNeeded)
                {
                    return result;
                }

                if (round >= MaxSspiRounds)
                {
                    return TdsAuthenticationResult.Fail("Login failed: the SSPI exchange did not complete.");
                }

                // MS-TDS 2.2.7.22: the server sends its token in an SSPI token, and the client answers with an SSPI message
                await writer.WriteAsync(TdsPacketType.TabularResult, TdsSspiMessage.CreateSspiToken(result.SspiToken.Span), cancellationToken).ConfigureAwait(false);

                var packet = await TdsPacketReader.ReadAsync(input, _options.MaxMessageSize, cancellationToken).ConfigureAwait(false);
                if (packet is null)
                {
                    return null;
                }

                if (packet.Type != TdsPacketType.Sspi)
                {
                    _logger.LogDebug("Unexpected TDS packet type {PacketType} during the SSPI exchange", packet.Type);
                    return TdsAuthenticationResult.Fail("Login failed: the client did not answer the SSPI challenge.");
                }

                context.SspiToken = packet.Payload;
            }
        }
        finally
        {
            foreach (var value in context.Items.Values)
            {
                (value as IDisposable)?.Dispose();
            }
        }
    }

    private static byte[] CreateLoginSuccessResponse(TdsAuthenticationResult authenticationResult)
    {
        var loginSuccess = TdsResponseSerializer.CreateLoginSuccess(authenticationResult);
        if (authenticationResult.SspiToken.IsEmpty)
        {
            return loginSuccess;
        }

        // The last token of the exchange, such as the Kerberos mutual authentication token, comes before LOGINACK
        return [.. TdsSspiMessage.CreateSspiToken(authenticationResult.SspiToken.Span), .. loginSuccess];
    }
}
