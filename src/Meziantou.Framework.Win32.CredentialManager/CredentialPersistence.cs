namespace Meziantou.Framework.Win32;

/// <summary>Specifies the persistence of a credential in the Windows Credential Manager.</summary>
public enum CredentialPersistence : uint
{
    /// <summary>The credential persists for the duration of the current logon session.</summary>
    Session = 1,

    /// <summary>The credential persists for all subsequent logon sessions on the local machine.</summary>
    LocalMachine,

    /// <summary>The credential persists for all subsequent logon sessions and is available across the enterprise.</summary>
    Enterprise,
}
