using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The client side of the opening handshake of the WebSocket protocol (https://www.rfc-editor.org/rfc/rfc6455#section-4.1):
/// the headers of the request, and the validation of the response, including the negotiation of the permessage-deflate
/// extension (https://www.rfc-editor.org/rfc/rfc7692).
/// </summary>
internal static class WebSocketHandshake
{
    /// <summary>The GUID appended to the key to compute the <c>Sec-WebSocket-Accept</c> header (https://www.rfc-editor.org/rfc/rfc6455#section-1.3).</summary>
    private const string AcceptGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    /// <summary>The <c>Sec-WebSocket-Extensions</c> header of the request (https://websockets.spec.whatwg.org/#concept-websocket-establish, step 10).</summary>
    public const string PermessageDeflateOffer = "permessage-deflate; client_max_window_bits";

    /// <summary>Returns a new key: a randomly selected 16-byte value, base64-encoded (https://websockets.spec.whatwg.org/#concept-websocket-establish, step 5).</summary>
    public static string CreateKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

    /// <summary>The value of the <c>Sec-WebSocket-Accept</c> header of a response to a request with <paramref name="key"/>.</summary>
#pragma warning disable CA5350 // SHA-1 is the hash of the protocol, it is not used for security
    public static string ComputeAccept(string key) => Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + AcceptGuid)));
#pragma warning restore CA5350

    /// <summary>
    /// Validates the response to the opening handshake (https://websockets.spec.whatwg.org/#concept-websocket-establish,
    /// step 11, and https://www.rfc-editor.org/rfc/rfc6455#section-4.1, steps 2 to 6 of the validation of the server's
    /// response). Returns false when the WebSocket connection must fail; otherwise, returns the subprotocol and the
    /// extensions in use, and the options of the permessage-deflate extension when the server accepted it.
    /// </summary>
    public static bool Validate(IReadOnlyList<KeyValuePair<string, string>> headers, string key, IReadOnlyList<string> protocols, out string protocol, out string extensions, out System.Net.WebSockets.WebSocketDeflateOptions? deflateOptions)
    {
        protocol = "";
        extensions = "";
        deflateOptions = null;

        // https://websockets.spec.whatwg.org/#concept-websocket-establish, step 11.2: a subprotocol was requested, and the server
        // did not select one
        var protocolValues = HeaderList.GetDecodeAndSplit(headers, "Sec-WebSocket-Protocol");
        var protocolValue = HeaderList.Get(headers, "Sec-WebSocket-Protocol");
        if (protocols.Count > 0 && (protocolValue is null || protocolValue.Length == 0))
            return false;

        // Step 2: Upgrade: websocket
        if (HeaderList.Get(headers, "Upgrade") is not { } upgrade || !InfraAscii.EqualsIgnoreAsciiCase(upgrade, "websocket"))
            return false;

        // Step 3: the Connection header contains the "Upgrade" token
        if (FetchHeaders.ExtractTokenList(headers, "Connection", out _) is not { } connection || !connection.Exists(token => InfraAscii.EqualsIgnoreAsciiCase(token, "Upgrade")))
            return false;

        // Step 4: Sec-WebSocket-Accept
        if (HeaderList.Get(headers, "Sec-WebSocket-Accept") is not { } accept || accept != ComputeAccept(key))
            return false;

        // Step 5: the extensions must be the ones of the request
        if (HeaderList.Get(headers, "Sec-WebSocket-Extensions") is { } extensionsValue)
        {
            if (!TryNegotiateDeflate(extensionsValue, out deflateOptions))
                return false;

            extensions = extensionsValue;
        }

        // Step 6: the subprotocol must be one of the request (a single value)
        if (protocolValue is not null)
        {
            if (protocolValues is not [var selected] || !protocols.Contains(selected, StringComparer.Ordinal))
                return false;

            protocol = selected;
        }

        return true;
    }

    /// <summary>
    /// Parses the <c>Sec-WebSocket-Extensions</c> header of the response (https://www.rfc-editor.org/rfc/rfc6455#section-9.1):
    /// the only extension of the request is permessage-deflate, whose parameters are validated as specified by
    /// https://www.rfc-editor.org/rfc/rfc7692#section-7.1.
    /// </summary>
    private static bool TryNegotiateDeflate(string value, out System.Net.WebSockets.WebSocketDeflateOptions? options)
    {
        options = null;
        if (value.Length == 0)
            return true;

        if (!TryParseExtensions(value, out var parsedExtensions) || parsedExtensions is not [("permessage-deflate", var parameters)])
            return false;

        var result = new System.Net.WebSockets.WebSocketDeflateOptions();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, parameterValue) in parameters)
        {
            // A parameter must not appear twice (https://www.rfc-editor.org/rfc/rfc7692#section-7.1)
            if (!names.Add(name))
                return false;

            switch (name)
            {
                case "server_no_context_takeover" when parameterValue is null:
                    result.ServerContextTakeover = false;
                    break;
                case "client_no_context_takeover" when parameterValue is null:
                    result.ClientContextTakeover = false;
                    break;
                case "server_max_window_bits" when TryParseWindowBits(parameterValue, out var serverBits):
                    // The messages of the server are inflated with a window of at least 9 bits (the minimum of zlib), which
                    // inflates the messages deflated with a window of 8 bits
                    result.ServerMaxWindowBits = Math.Max(serverBits, 9);
                    break;

                // The request has client_max_window_bits: the server may limit the window of the messages of the client. The
                // deflater of zlib cannot use a window of 8 bits (it uses 9 bits), so the server cannot inflate them
                case "client_max_window_bits" when TryParseWindowBits(parameterValue, out var clientBits) && clientBits >= 9:
                    result.ClientMaxWindowBits = clientBits;
                    break;
                default:
                    return false;
            }
        }

        options = result;
        return true;
    }

    private static bool TryParseWindowBits(string? value, out int bits)
    {
        bits = 0;
        return value is { Length: 1 or 2 } && value[0] != '0' && value.All(char.IsAsciiDigit)
            && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out bits) && bits is >= 8 and <= 15;
    }

    /// <summary>
    /// Parses the value of a <c>Sec-WebSocket-Extensions</c> header (https://www.rfc-editor.org/rfc/rfc6455#section-9.1):
    /// a list of extensions, each with its parameters, whose values may be quoted strings.
    /// </summary>
    private static bool TryParseExtensions(string value, out List<(string Name, List<(string Name, string? Value)> Parameters)> extensions)
    {
        extensions = [];
        var position = 0;
        while (true)
        {
            SkipWhitespace(value, ref position);
            if (ReadToken(value, ref position) is not { } extensionName)
                return false;

            var parameters = new List<(string Name, string? Value)>();
            SkipWhitespace(value, ref position);
            while (position < value.Length && value[position] == ';')
            {
                position++;
                SkipWhitespace(value, ref position);
                if (ReadToken(value, ref position) is not { } parameterName)
                    return false;

                string? parameterValue = null;
                SkipWhitespace(value, ref position);
                if (position < value.Length && value[position] == '=')
                {
                    position++;
                    SkipWhitespace(value, ref position);
                    if (position < value.Length && value[position] == '"')
                    {
                        // A quoted value must be a token once unquoted (https://www.rfc-editor.org/rfc/rfc6455#section-9.1)
                        var end = value.IndexOf('"', position + 1, StringComparison.Ordinal);
                        if (end < 0)
                            return false;

                        parameterValue = value[(position + 1)..end];
                        position = end + 1;
                        if (!FetchHeaders.IsToken(parameterValue))
                            return false;
                    }
                    else if (ReadToken(value, ref position) is { } token)
                    {
                        parameterValue = token;
                    }
                    else
                    {
                        return false;
                    }

                    SkipWhitespace(value, ref position);
                }

                parameters.Add((parameterName, parameterValue));
            }

            extensions.Add((extensionName, parameters));
            if (position == value.Length)
                return true;

            if (value[position] != ',')
                return false;

            position++;
        }

        static void SkipWhitespace(string value, ref int position)
        {
            while (position < value.Length && value[position] is ' ' or '\t')
            {
                position++;
            }
        }

        static string? ReadToken(string value, ref int position)
        {
            var start = position;
            while (position < value.Length && FetchHeaders.IsTokenCharacter(value[position]))
            {
                position++;
            }

            return position == start ? null : value[start..position];
        }
    }
}
