using System.Net;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace Meziantou.Framework.PostgreSql.Handler;

/// <summary>Provides context for an authentication request.</summary>
public sealed class PostgreSqlAuthenticationContext
{
    /// <summary>Gets the remote endpoint of the client.</summary>
    public required EndPoint RemoteEndPoint { get; init; }

    /// <summary>Gets the authentication method requested by the server.</summary>
    public required PostgreSqlAuthenticationMethod Method { get; init; }

    /// <summary>Gets the user name sent by the client.</summary>
    public string? UserName { get; init; }

    /// <summary>Gets the requested initial database, if any.</summary>
    public string? Database { get; init; }

    /// <summary>Gets startup parameters sent by the client.</summary>
    public IReadOnlyDictionary<string, string> StartupParameters { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets the cleartext password value sent by the client when available.</summary>
    public string? Password { get; internal init; }

    /// <summary>Gets the raw MD5 password response sent by the client when available.</summary>
    public string? Md5PasswordResponse { get; internal init; }

    internal byte[]? Md5Salt { get; init; }

    internal string? ScramAuthMessage { get; init; }

    internal byte[]? ScramClientProof { get; init; }

    internal byte[]? ScramSalt { get; init; }

    internal int ScramIterationCount { get; init; }

    internal string? ScramServerFinalMessage { get; private set; }

    /// <summary>
    /// Validates the client authentication response against the expected password.
    /// For SCRAM-SHA-256, this method also computes the server-final message sent to the client.
    /// </summary>
    public bool ValidatePassword(string expectedPassword)
    {
        ArgumentNullException.ThrowIfNull(expectedPassword);

        return Method switch
        {
            PostgreSqlAuthenticationMethod.ClearTextPassword => ValidateClearTextPassword(expectedPassword),
            PostgreSqlAuthenticationMethod.Md5Password => ValidateMd5Password(expectedPassword),
            PostgreSqlAuthenticationMethod.ScramSha256 => ValidateScramSha256Password(expectedPassword),
            _ => false,
        };
    }

    internal bool TryGetScramServerFinalMessage([NotNullWhen(true)] out string? message)
    {
        message = ScramServerFinalMessage;
        return !string.IsNullOrEmpty(message);
    }

    private bool ValidateClearTextPassword(string expectedPassword)
    {
        return string.Equals(Password, expectedPassword, StringComparison.Ordinal);
    }

    private bool ValidateMd5Password(string expectedPassword)
    {
        if (string.IsNullOrEmpty(UserName) || string.IsNullOrEmpty(Md5PasswordResponse) || Md5Salt is null || Md5Salt.Length != 4)
        {
            return false;
        }

        var passwordAndUserNameHash = ComputeMd5Hex(Encoding.UTF8.GetBytes(expectedPassword + UserName));
        var secondStepInput = Encoding.UTF8.GetBytes(passwordAndUserNameHash);
        var bytesToHash = new byte[secondStepInput.Length + Md5Salt.Length];
        secondStepInput.CopyTo(bytesToHash, 0);
        Md5Salt.CopyTo(bytesToHash, secondStepInput.Length);
        var expectedResponse = "md5" + ComputeMd5Hex(bytesToHash);
        return string.Equals(expectedResponse, Md5PasswordResponse, StringComparison.OrdinalIgnoreCase);
    }

    private bool ValidateScramSha256Password(string expectedPassword)
    {
        if (ScramSalt is null ||
            ScramSalt.Length == 0 ||
            ScramIterationCount <= 0 ||
            ScramClientProof is null ||
            ScramClientProof.Length == 0 ||
            string.IsNullOrEmpty(ScramAuthMessage))
        {
            return false;
        }

        var authMessageBytes = Encoding.UTF8.GetBytes(ScramAuthMessage);
        var saltedPassword = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(expectedPassword), ScramSalt, ScramIterationCount, HashAlgorithmName.SHA256, 32);
        var clientKey = ComputeHmac(saltedPassword, "Client Key");
        var storedKey = SHA256.HashData(clientKey);
        var clientSignature = ComputeHmac(storedKey, authMessageBytes);
        if (clientSignature.Length != ScramClientProof.Length)
        {
            return false;
        }

        var recoveredClientKey = new byte[clientSignature.Length];
        for (var i = 0; i < recoveredClientKey.Length; i++)
        {
            recoveredClientKey[i] = (byte)(ScramClientProof[i] ^ clientSignature[i]);
        }

        var recoveredStoredKey = SHA256.HashData(recoveredClientKey);
        if (!CryptographicOperations.FixedTimeEquals(storedKey, recoveredStoredKey))
        {
            return false;
        }

        var serverKey = ComputeHmac(saltedPassword, "Server Key");
        var serverSignature = ComputeHmac(serverKey, authMessageBytes);
        ScramServerFinalMessage = "v=" + Convert.ToBase64String(serverSignature);
        return true;
    }

    private static byte[] ComputeHmac(byte[] key, string data)
    {
        return ComputeHmac(key, Encoding.UTF8.GetBytes(data));
    }

    private static byte[] ComputeHmac(byte[] key, byte[] data)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(data);
    }

    [SuppressMessage("Security", "CA5351:Do Not Use Broken Cryptographic Algorithms", Justification = "PostgreSQL MD5 authentication requires MD5 hashing.")]
    private static string ComputeMd5Hex(byte[] bytes)
    {
        var hash = MD5.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
