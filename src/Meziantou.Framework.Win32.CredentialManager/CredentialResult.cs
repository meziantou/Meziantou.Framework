namespace Meziantou.Framework.Win32;

/// <summary>Represents the result of a credential prompt operation.</summary>
public sealed class CredentialResult
{
    /// <summary>Initializes a new instance of the <see cref="CredentialResult"/> class.</summary>
    /// <param name="userName">The username.</param>
    /// <param name="password">The password.</param>
    /// <param name="domain">The domain name.</param>
    /// <param name="credentialSaved">Indicates whether the user chose to save the credentials.</param>
    public CredentialResult(string userName, string password, string? domain, CredentialSaveOption credentialSaved)
    {
        UserName = userName;
        Password = password;
        Domain = domain;
        CredentialSaved = credentialSaved;
    }

    /// <summary>Gets the username.</summary>
    public string UserName { get; }

    /// <summary>Gets the password.</summary>
    public string Password { get; }

    /// <summary>Gets the domain name.</summary>
    public string? Domain { get; }

    /// <summary>Gets a value indicating whether the user chose to save the credentials.</summary>
    public CredentialSaveOption CredentialSaved { get; }

    public override string ToString() => $"Domain: {Domain}; Username: {UserName}; Password: {(Password is null ? "" : "******")}; Saved: {CredentialSaved}";
}
