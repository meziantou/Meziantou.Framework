using System.Security.Cryptography;
using System.Diagnostics.CodeAnalysis;
using System.Text;

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

    public static bool TryParseClientFirstMessage(string message, [NotNullWhen(true)] out string? clientFirstMessageBare, [NotNullWhen(true)] out string? clientNonce)
    {
        clientFirstMessageBare = null;
        clientNonce = null;
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        var gs2HeaderSeparatorIndex = message.IndexOf(",,", StringComparison.Ordinal);
        if (gs2HeaderSeparatorIndex <= 0 || gs2HeaderSeparatorIndex + 2 >= message.Length)
        {
            return false;
        }

        clientFirstMessageBare = message[(gs2HeaderSeparatorIndex + 2)..];
        var attributes = ParseAttributes(clientFirstMessageBare);
        return attributes.TryGetValue("r", out clientNonce) && !string.IsNullOrWhiteSpace(clientNonce);
    }

    public static bool TryParseClientFinalMessage(string message, [NotNullWhen(true)] out string? withoutProof, [NotNullWhen(true)] out byte[]? proof, [NotNullWhen(true)] out string? nonce)
    {
        withoutProof = null;
        proof = null;
        nonce = null;
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

        proof = Convert.FromBase64String(proofValue);
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
