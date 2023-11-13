using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;

namespace Meziantou.Framework.Win32;

[SupportedOSPlatform("windows5.1.2600")]
public sealed class SecurityIdentifier : IEquatable<SecurityIdentifier?>
{
    private const byte MaxSubAuthorities = 15;
    private const int MaxBinaryLength = 1 + 1 + 6 + (MaxSubAuthorities * 4); // 4 bytes for each subauth

    internal SecurityIdentifier(PSID sid)
    {
        if (sid == default)
            throw new ArgumentNullException(nameof(sid));

        LookupName(sid, out var domain, out var name);
        Domain = domain;
        Name = name;
        Sid = ConvertSidToStringSid(sid);
    }

    public string? Domain { get; }
    public string? Name { get; }
    public string Sid { get; }

    public string FullName => Domain + "\\" + Name;

    public override string ToString()
    {
        if (Name is null)
            return Sid;

        return FullName;
    }

    public static unsafe SecurityIdentifier FromWellKnown(WellKnownSidType type)
    {
        uint size = MaxBinaryLength * sizeof(byte);
        var resultSid = new PSID((void*)Marshal.AllocHGlobal((int)size));

        try
        {
            if (!PInvoke.CreateWellKnownSid((WELL_KNOWN_SID_TYPE)type, default, resultSid, ref size))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            return new SecurityIdentifier(resultSid);
        }
        finally
        {
            if (resultSid.Value is not null)
            {
                Marshal.FreeHGlobal((nint)resultSid.Value);
            }
        }
    }

    private static string ConvertSidToStringSid(PSID sid)
    {
        if (PInvoke.ConvertSidToStringSid(sid, out var result))
            return result.ToString();

        throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static unsafe void LookupName(PSID sid, out string? domain, out string? name)
    {
        var userNameLen = 256u;
        var domainNameLen = 256u;

        fixed (char* userName = new char[userNameLen])
        fixed (char* domainName = new char[domainNameLen])
        {
            if (PInvoke.LookupAccountSid(lpSystemName: null, sid, userName, ref userNameLen, domainName, ref domainNameLen, out _) != 0)
            {
                domain = new string(userName, 0, (int)domainNameLen);
                name = new string(domainName, 0, (int)userNameLen);
                return;
            }

            var error = Marshal.GetLastWin32Error();
            if (error == (int)WIN32_ERROR.ERROR_NONE_MAPPED)
            {
                domain = default;
                name = default;
                return;
            }

            throw new Win32Exception(error);
        }
    }

    public bool Equals(SecurityIdentifier? other)
    {
        return other != null && Sid == other.Sid;
    }

    public override bool Equals(object? obj) => Equals(obj as SecurityIdentifier);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Sid);

    public static bool operator ==(SecurityIdentifier? left, SecurityIdentifier? right) => EqualityComparer<SecurityIdentifier>.Default.Equals(left, right);

    public static bool operator !=(SecurityIdentifier? left, SecurityIdentifier? right) => !(left == right);
}
