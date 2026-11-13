using System.Text.Json;

namespace Meziantou.Framework.WebPlatform.DevTools.Protocol;

/// <summary>A command of a client: <c>{ "id": 1, "method": "Domain.command", "params": { ... }, "sessionId": "..." }</c>.</summary>
internal readonly record struct CdpMessage(long Id, string Method, JsonElement Parameters, string? SessionId)
{
    /// <summary>The code of the messages that are not valid JSON.</summary>
    public const int ParseError = -32700;

    /// <summary>The code of the messages that are not valid commands.</summary>
    public const int InvalidRequest = -32600;

    /// <summary>
    /// Parses a message of a client. Returns false with an error (its code and message) when the message is not a valid command;
    /// <paramref name="id"/> is then the identifier of the message, when it has one.
    /// </summary>
    public static bool TryParse(ReadOnlyMemory<byte> utf8, out CdpMessage message, out long? id, out int errorCode, out string? errorMessage)
    {
        message = default;
        id = null;
        errorCode = 0;
        errorMessage = null;

        // As Chromium, a message that is not valid UTF-8 is not valid JSON (the reader only validates the strings it decodes)
        if (!System.Text.Unicode.Utf8.IsValid(utf8.Span))
        {
            errorCode = ParseError;
            errorMessage = "Message must be a valid JSON";
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8, new JsonDocumentOptions { MaxDepth = 256 });
        }
        catch (JsonException)
        {
            errorCode = ParseError;
            errorMessage = "Message must be a valid JSON";
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                errorCode = ParseError;
                errorMessage = "Message must be an object";
                return false;
            }

            if (!root.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.Number || !idElement.TryGetInt64(out var messageId))
            {
                errorCode = InvalidRequest;
                errorMessage = "Message must have integer 'id' property";
                return false;
            }

            id = messageId;
            if (!root.TryGetProperty("method", out var methodElement) || methodElement.ValueKind != JsonValueKind.String)
            {
                errorCode = InvalidRequest;
                errorMessage = "Message must have string 'method' property";
                return false;
            }

            string? sessionId = null;
            if (root.TryGetProperty("sessionId", out var sessionElement))
            {
                if (sessionElement.ValueKind != JsonValueKind.String)
                {
                    errorCode = InvalidRequest;
                    errorMessage = "Message must have string 'sessionId' property";
                    return false;
                }

                sessionId = sessionElement.GetString();
            }

            var parameters = default(JsonElement);
            if (root.TryGetProperty("params", out var parametersElement) && parametersElement.ValueKind is not JsonValueKind.Null)
            {
                if (parametersElement.ValueKind != JsonValueKind.Object)
                {
                    errorCode = InvalidRequest;
                    errorMessage = "Message has property other than 'id', 'method', 'sessionId', 'params'";
                    return false;
                }

                parameters = parametersElement.Clone();
            }

            message = new CdpMessage(messageId, methodElement.GetString()!, parameters, sessionId);
            return true;
        }
    }
}
