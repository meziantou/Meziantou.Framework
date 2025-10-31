namespace Meziantou.Framework.Win32;

/// <summary>Specifies the type of a credential in the Windows Credential Manager.</summary>
public enum CredentialType
{
    /// <summary>A generic credential.</summary>
    Generic = 1,

    /// <summary>A password credential for a domain or server.</summary>
    DomainPassword,

    /// <summary>A certificate credential for a domain or server.</summary>
    DomainCertificate,

    /// <summary>A password credential that is stored in plaintext.</summary>
    DomainVisiblePassword,

    /// <summary>A generic certificate credential.</summary>
    GenericCertificate,

    /// <summary>An extended domain credential.</summary>
    DomainExtended,

    /// <summary>The maximum number of supported credential types.</summary>
    Maximum,

    /// <summary>The maximum number of supported credential types including extended types.</summary>
    MaximumEx = Maximum + 1000,
}
