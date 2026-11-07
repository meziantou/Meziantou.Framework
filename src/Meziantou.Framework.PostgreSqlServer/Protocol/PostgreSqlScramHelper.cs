using System.Security.Cryptography;

namespace Meziantou.Framework.PostgreSql.Protocol;

internal static class PostgreSqlScramHelper
{
    public static string GenerateNonce()
    {
        var bytes = new byte[18];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    public static Dictionary<string, string> ParseAttributes(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var segments = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var segment in segments)
        {
            var equalIndex = segment.IndexOf('=', StringComparison.Ordinal);
            if (equalIndex <= 0 || equalIndex >= segment.Length - 1)
            {
                continue;
            }

            var key = segment[..equalIndex];
            var attributeValue = segment[(equalIndex + 1)..];
            result[key] = attributeValue;
        }

        return result;
    }

    public static bool TryParseClientFirstMessage(
        string message,
        [NotNullWhen(true)] out string? clientFirstMessageBare,
        [NotNullWhen(true)] out string? clientNonce,
        [NotNullWhen(true)] out string? gs2Header)
    {
        clientFirstMessageBare = null;
        clientNonce = null;
        gs2Header = null;
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        var gs2HeaderSeparatorIndex = message.IndexOf(",,", StringComparison.Ordinal);
        if (gs2HeaderSeparatorIndex <= 0 || gs2HeaderSeparatorIndex + 2 >= message.Length)
        {
            return false;
        }

        // The gs2 flag is 'n' (client does not support channel binding) or 'y' (supports it but the server did
        // not offer it). 'p' means the client demands channel binding, which this server never advertises.
        gs2Header = message[..(gs2HeaderSeparatorIndex + 2)];
        var flag = message[0];
        if (flag is not 'n' and not 'y')
        {
            return false;
        }

        clientFirstMessageBare = message[(gs2HeaderSeparatorIndex + 2)..];
        var attributes = ParseAttributes(clientFirstMessageBare);
        return attributes.TryGetValue("r", out clientNonce) && !string.IsNullOrWhiteSpace(clientNonce);
    }

    /// <summary>Verifies that the client-final <c>c=</c> attribute repeats the gs2 header sent in client-first.</summary>
    public static bool IsExpectedChannelBinding(string? channelBinding, string gs2Header)
    {
        if (channelBinding is null)
        {
            return false;
        }

        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes(gs2Header));
        return string.Equals(channelBinding, expected, StringComparison.Ordinal);
    }

    public static bool TryParseClientFinalMessage(
        string message,
        [NotNullWhen(true)] out string? withoutProof,
        [NotNullWhen(true)] out byte[]? proof,
        [NotNullWhen(true)] out string? nonce,
        out string? channelBinding)
    {
        withoutProof = null;
        proof = null;
        nonce = null;
        channelBinding = null;
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var attributes = ParseAttributes(message);
        if (!attributes.TryGetValue("p", out var proofValue) ||
            !attributes.TryGetValue("r", out nonce) ||
            string.IsNullOrWhiteSpace(nonce))
        {
            return false;
        }

        _ = attributes.TryGetValue("c", out channelBinding);

        // Honour the Try contract: malformed base64 is a parse failure, not an exception.
        var proofBuffer = new byte[((proofValue.Length + 3) / 4) * 3];
        if (!Convert.TryFromBase64String(proofValue, proofBuffer, out var proofLength))
        {
            return false;
        }

        proof = proofBuffer[..proofLength];
        var proofStart = message.IndexOf(",p=", StringComparison.Ordinal);
        if (proofStart < 0)
        {
            return false;
        }

        withoutProof = message[..proofStart];
        return true;
    }

    public static string BuildServerFirstMessage(string fullNonce, byte[] salt, int iterationCount)
    {
        ArgumentException.ThrowIfNullOrEmpty(fullNonce);
        ArgumentNullException.ThrowIfNull(salt);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(iterationCount);

        return $"r={fullNonce},s={Convert.ToBase64String(salt)},i={iterationCount}";
    }

    public static byte[] CreateSalt(int length = 16)
    {
        var salt = new byte[length];
        RandomNumberGenerator.Fill(salt);
        return salt;
    }
}
