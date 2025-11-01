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

    /// <summary>A sentinel value indicating the maximum number of supported credential types. This is used internally by the Windows API and should not be used as an actual credential type.</summary>
    Maximum,

    /// <summary>A sentinel value indicating the maximum number of supported credential types including extended types. This is used internally by the Windows API and should not be used as an actual credential type.</summary>
    MaximumEx = Maximum + 1000,
}
