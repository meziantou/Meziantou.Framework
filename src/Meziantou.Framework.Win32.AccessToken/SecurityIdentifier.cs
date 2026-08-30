using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;

namespace Meziantou.Framework.Win32;

/// <summary>Represents a security identifier (SID) that uniquely identifies a user, group, or other security principal.</summary>
/// <example>
/// <code>
/// // Get a well-known SID
/// var adminSid = SecurityIdentifier.FromWellKnown(WellKnownSidType.WinBuiltinAdministratorsSid);
/// Console.WriteLine($"Administrators SID: {adminSid.Sid}");
/// Console.WriteLine($"Full Name: {adminSid.FullName}");
///
/// // Compare SIDs
/// using var token = AccessToken.OpenCurrentProcessToken(TokenAccessLevels.Query);
/// var ownerSid = token.GetOwner();
/// if (ownerSid == adminSid)
/// {
///     Console.WriteLine("Owner is administrator");
/// }
/// </code>
/// </example>
/// <remarks>
/// A SID is a variable-length structure that uniquely identifies a security principal in Windows.
/// This class provides methods to resolve SIDs to account names and domains.
/// </remarks>
[SupportedOSPlatform("windows5.1.2600")]
public sealed class SecurityIdentifier : IEquatable<SecurityIdentifier?>
{
    private const byte MaxSubAuthorities = 15;
    private const int MaxBinaryLength = 1 + 1 + 6 + (MaxSubAuthorities * 4); // 4 bytes for each subauth

    private readonly byte[] _sid;
    private (string? Domain, string? Name)? _account;

    internal unsafe SecurityIdentifier(PSID sid)
    {
        if (sid == default)
            throw new ArgumentNullException(nameof(sid));

        // The SID is only valid for the lifetime of the caller's buffer, so keep a copy for the deferred lookup
        _sid = new Span<byte>(sid.Value, (int)PInvoke.GetLengthSid(sid)).ToArray();
        Sid = ConvertSidToStringSid(sid);
    }

    /// <summary>Gets the domain name associated with the SID.</summary>
    /// <value>The domain name, or <see langword="null"/> if the SID could not be resolved.</value>
    /// <remarks>Resolved on first access, which may query the local security authority or a domain controller.</remarks>
    /// <exception cref="Win32Exception">The account lookup failed.</exception>
    public string? Domain => Account.Domain;

    /// <summary>Gets the account name associated with the SID.</summary>
    /// <value>The account name, or <see langword="null"/> if the SID could not be resolved.</value>
    /// <remarks>Resolved on first access, which may query the local security authority or a domain controller.</remarks>
    /// <exception cref="Win32Exception">The account lookup failed.</exception>
    public string? Name => Account.Name;

    private unsafe (string? Domain, string? Name) Account
    {
        get
        {
            if (_account is null)
            {
                fixed (byte* pointer = _sid)
                {
                    LookupName(new PSID(pointer), out var domain, out var name);
                    _account = (domain, name);
                }
            }

            return _account.Value;
        }
    }

    /// <summary>Gets the string representation of the SID (e.g., "S-1-5-21-...").</summary>
    public string Sid { get; }

    /// <summary>Gets the full name in the format "Domain\Name".</summary>
    public string FullName => Domain + "\\" + Name;

    /// <summary>Returns a string representation of the security identifier.</summary>
    /// <returns>The full name if available; otherwise, the SID string.</returns>
    public override string ToString()
    {
        if (Name is null)
            return Sid;

        return FullName;
    }

    /// <summary>Creates a <see cref="SecurityIdentifier"/> for a well-known SID type.</summary>
    /// <param name="type">The well-known SID type to create.</param>
    /// <returns>A <see cref="SecurityIdentifier"/> representing the well-known SID.</returns>
    /// <exception cref="Win32Exception">The SID could not be created.</exception>
    /// <example>
    /// <code>
    /// var adminSid = SecurityIdentifier.FromWellKnown(WellKnownSidType.WinBuiltinAdministratorsSid);
    /// var systemSid = SecurityIdentifier.FromWellKnown(WellKnownSidType.WinLocalSystemSid);
    /// </code>
    /// </example>
    public static unsafe SecurityIdentifier FromWellKnown(WellKnownSidType type)
    {
        uint size = MaxBinaryLength * sizeof(byte);
        var resultSid = new PSID((void*)Marshal.AllocHGlobal((int)size));

        try
        {
            if (!PInvoke.CreateWellKnownSid((WELL_KNOWN_SID_TYPE)type, default, resultSid, &size))
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

    private static unsafe string ConvertSidToStringSid(PSID sid)
    {
        PWSTR result = default;
        if (!PInvoke.ConvertSidToStringSid(sid, &result))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        try
        {
            return result.ToString();
        }
        finally
        {
            // ConvertSidToStringSid allocates the buffer with LocalAlloc
            PInvoke.LocalFree((HLOCAL)result.Value);
        }
    }

    private static unsafe void LookupName(PSID sid, out string? domain, out string? name)
    {
        var userNameLen = 256u;
        var domainNameLen = 256u;

        // LookupAccountSid reports the required sizes when a buffer is too small, so retry once with them
        for (var attempt = 0; ; attempt++)
        {
            fixed (char* userName = new char[userNameLen])
            fixed (char* domainName = new char[domainNameLen])
            {
                SID_NAME_USE sidType;
                if (PInvoke.LookupAccountSid(lpSystemName: null, sid, userName, &userNameLen, domainName, &domainNameLen, &sidType) != 0)
                {
                    name = new string(userName, 0, (int)userNameLen);
                    domain = new string(domainName, 0, (int)domainNameLen);
                    return;
                }

                var error = Marshal.GetLastWin32Error();
                if (error == (int)WIN32_ERROR.ERROR_NONE_MAPPED)
                {
                    domain = default;
                    name = default;
                    return;
                }

                if (error != (int)WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER || attempt > 0)
                    throw new Win32Exception(error);
            }
        }
    }

    /// <summary>Determines whether the specified <see cref="SecurityIdentifier"/> is equal to the current instance.</summary>
    public bool Equals([NotNullWhen(true)] SecurityIdentifier? other)
    {
        return other != null && Sid == other.Sid;
    }

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is SecurityIdentifier other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Sid);

    public static bool operator ==(SecurityIdentifier? left, SecurityIdentifier? right) => EqualityComparer<SecurityIdentifier>.Default.Equals(left, right);

    public static bool operator !=(SecurityIdentifier? left, SecurityIdentifier? right) => !(left == right);
}
