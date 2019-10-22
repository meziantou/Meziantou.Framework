using System;

namespace Meziantou.Framework.Win32.Natives
{
    [Flags]
    internal enum CredentialUIFlags
    {
        IncorrectPassword = 0x1,
        DoNotPersist = 0x2,
        RequestAdministrator = 0x4,
        ExcludeCertificates = 0x8,
        RequireCertificate = 0x10,
        ShowSaveCheckBox = 0x40,
        AlwaysShowUi = 0x80,
        RequireSmartcard = 0x100,
        PasswordOnlyOk = 0x200,
        ValidateUsername = 0x400,
        CompleteUsername = 0x800,
        Persist = 0x1000,
        ServerCredential = 0x4000,
        ExpectConfirmation = 0x20000,
        GenericCredentials = 0x40000,
        UsernameTargetCredentials = 0x80000,
        KeepUsername = 0x100000,
    }
}
