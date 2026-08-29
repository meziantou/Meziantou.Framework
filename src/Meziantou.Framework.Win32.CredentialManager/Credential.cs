using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32;

/// <summary>Represents a credential stored in the Windows Credential Manager.</summary>
public sealed class Credential
{
    /// <summary>Gets the type of the credential.</summary>
    public CredentialType CredentialType { get; }

    /// <summary>Gets the name that identifies the credential.</summary>
    public string ApplicationName { get; }

    /// <summary>Gets the username associated with the credential.</summary>
    public string? UserName { get; }

    /// <summary>Gets the password or secret associated with the credential, decoded as UTF-16.</summary>
    /// <remarks>
    /// Decoding is lossy when the credential holds arbitrary binary data: a trailing odd byte is dropped, and
    /// bytes that do not form valid UTF-16 do not survive being encoded again. Use <see cref="Secret"/> to get
    /// the bytes exactly as Windows stored them.
    /// </remarks>
    public string? Password { get; }

    /// <summary>Gets the secret associated with the credential, exactly as stored by Windows.</summary>
    /// <remarks>
    /// For <see cref="CredentialType.Generic"/> credentials the layout of the blob is defined by the application
    /// that wrote it, so it may hold arbitrary bytes rather than text. The value is empty when the credential
    /// carries no secret.
    /// </remarks>
    public ReadOnlyMemory<byte> Secret { get; }

    /// <summary>Gets the comment describing the credential.</summary>
    public string? Comment { get; }

    /// <summary>Initializes a new instance of the <see cref="Credential"/> class.</summary>
    /// <param name="credentialType">The type of the credential.</param>
    /// <param name="applicationName">The name that identifies the credential.</param>
    /// <param name="userName">The username.</param>
    /// <param name="password">The password or secret.</param>
    /// <param name="comment">An optional comment describing the credential.</param>
    public Credential(CredentialType credentialType, string applicationName, string? userName, string? password, string? comment)
    {
        ApplicationName = applicationName;
        UserName = userName;
        Password = password;
        Secret = password is null ? default : MemoryMarshal.AsBytes(password.AsSpan()).ToArray();
        CredentialType = credentialType;
        Comment = comment;
    }

    /// <summary>Initializes a new instance of the <see cref="Credential"/> class from a raw secret.</summary>
    /// <param name="credentialType">The type of the credential.</param>
    /// <param name="applicationName">The name that identifies the credential.</param>
    /// <param name="userName">The username.</param>
    /// <param name="secret">The secret, as stored by Windows. <see cref="Password"/> exposes it decoded as UTF-16.</param>
    /// <param name="comment">An optional comment describing the credential.</param>
    public Credential(CredentialType credentialType, string applicationName, string? userName, ReadOnlyMemory<byte> secret, string? comment)
    {
        ApplicationName = applicationName;
        UserName = userName;
        Secret = secret;

        // The blob does not have to contain a whole number of UTF-16 code units, so ignore a trailing odd byte.
        Password = MemoryMarshal.Cast<byte, char>(secret.Span[..(secret.Length - (secret.Length % 2))]).ToString();
        CredentialType = credentialType;
        Comment = comment;
    }

    public override string ToString()
    {
        return $"CredentialType: {CredentialType}, ApplicationName: {ApplicationName}, UserName: {UserName}, Password: {Password}, Comment: {Comment}";
    }
}
