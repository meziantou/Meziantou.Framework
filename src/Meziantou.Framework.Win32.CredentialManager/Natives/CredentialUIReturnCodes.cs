namespace Meziantou.Framework.Win32.Natives
{
    internal enum CredentialUIReturnCodes : uint
    {
        Success = 0,
        Cancelled = 1223,
        NoSuchLogonSession = 1312,
        NotFound = 1168,
        InvalidAccountName = 1315,
        InsufficientBuffer = 122,
        InvalidParameter = 87,
        InvalidFlags = 1004,
    }
}
