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

    /// <summary>Gets the password or secret associated with the credential.</summary>
    public string? Password { get; }

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
        CredentialType = credentialType;
        Comment = comment;
    }

    public override string ToString()
    {
        return $"CredentialType: {CredentialType}, ApplicationName: {ApplicationName}, UserName: {UserName}, Password: {Password}, Comment: {Comment}";
    }
}
