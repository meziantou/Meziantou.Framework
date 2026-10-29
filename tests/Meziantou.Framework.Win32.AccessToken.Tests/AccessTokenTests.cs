using System.ComponentModel;
using System.Diagnostics;
using Meziantou.Xunit;

namespace Meziantou.Framework.Win32.Tests;

public sealed class AccessTokenTests
{
    private readonly ITestOutputHelper _output;

    public AccessTokenTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void ReadmeExample_Open()
    {
        #region AccessToken_Open
        // Open the current process token
        using var token = AccessToken.OpenCurrentProcessToken(TokenAccessLevels.Query);

        // Open a token for a specific process
        using var process = Process.GetCurrentProcess();
        using var processToken = AccessToken.OpenProcessToken(process, TokenAccessLevels.Query);
        #endregion

        Assert.Equal(token.GetOwner(), processToken.GetOwner());
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void ReadmeExample_Query()
    {
        #region AccessToken_Query
        using var token = AccessToken.OpenCurrentProcessToken(TokenAccessLevels.Query);

        // Get token type (Primary or Impersonation)
        var tokenType = token.GetTokenType();
        Assert.Equal(TokenType.TokenPrimary, tokenType);

        // Check if token is elevated
        var isElevated = token.IsElevated();

        // Get elevation type (Unknown, Default, Full, or Limited)
        var elevationType = token.GetElevationType();
        Assert.NotEqual(TokenElevationType.Unknown, elevationType);
        if (elevationType == TokenElevationType.Limited)
        {
            // The filtered token of an administrator when UAC is enabled
            Assert.False(isElevated);
        }

        // Check if token is restricted
        var isRestricted = token.IsRestricted();
        Assert.False(isRestricted);

        // Get token owner
        var owner = token.GetOwner();
        Assert.StartsWith("S-1-", owner.Sid);

        // Get mandatory integrity level
        var integrityLevel = token.GetMandatoryIntegrityLevel();
        Assert.StartsWith("S-1-16-", integrityLevel.Sid.Sid);

        // Enumerate all groups
        var everyone = SecurityIdentifier.FromWellKnown(WellKnownSidType.WinWorldSid);
        Assert.Contains(token.EnumerateGroups(), group => group.Sid == everyone);

        // Enumerate restricted SIDs
        Assert.Empty(token.EnumerateRestrictedSid());

        // Enumerate all privileges
        Assert.Contains(token.EnumeratePrivileges(), privilege => privilege.Name == Privileges.SE_CHANGE_NOTIFY_NAME);
        #endregion
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void ReadmeExample_IsAdministrator()
    {
        #region AccessToken_IsAdministrator
        bool IsAdministrator()
        {
            using var token = AccessToken.OpenCurrentProcessToken(TokenAccessLevels.Query);

            // Check if current token has admin rights
            if (IsAdministrator(token))
                return true;

            if (token.GetElevationType() == TokenElevationType.Limited)
            {
                // If limited, check the linked token (elevated token)
                using var linkedToken = token.GetLinkedToken();
                return linkedToken is not null && IsAdministrator(linkedToken);
            }

            return false;

            static bool IsAdministrator(AccessToken accessToken)
            {
                var adminSid = SecurityIdentifier.FromWellKnown(WellKnownSidType.WinBuiltinAdministratorsSid);
                foreach (var group in accessToken.EnumerateGroups())
                {
                    if (group.Attributes.HasFlag(GroupSidAttributes.SE_GROUP_ENABLED) &&
                        group.Sid == adminSid)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
        #endregion

        Assert.Equal(AccessTokenTests.IsAdministrator(), IsAdministrator());
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void ReadmeExample_SecurityIdentifiers()
    {
        #region AccessToken_SecurityIdentifiers
        // Get SID from well-known type
        var adminSid = SecurityIdentifier.FromWellKnown(WellKnownSidType.WinBuiltinAdministratorsSid);
        Assert.Equal("S-1-5-32-544", adminSid.Sid);
        Assert.Equal($@"{adminSid.Domain}\{adminSid.Name}", adminSid.FullName); // BUILTIN\Administrators on an English Windows

        // Get well-known integrity level SIDs
        var lowIntegrity = SecurityIdentifier.FromWellKnown(WellKnownSidType.WinLowLabelSid);
        var mediumIntegrity = SecurityIdentifier.FromWellKnown(WellKnownSidType.WinMediumLabelSid);
        var highIntegrity = SecurityIdentifier.FromWellKnown(WellKnownSidType.WinHighLabelSid);
        Assert.Equal("S-1-16-4096", lowIntegrity.Sid);
        Assert.Equal("S-1-16-8192", mediumIntegrity.Sid);
        Assert.Equal("S-1-16-12288", highIntegrity.Sid);
        #endregion
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void AccessTokenTest()
    {
        using var token = AccessToken.OpenCurrentProcessToken(TokenAccessLevels.Query);
        PrintToken(token);

        Assert.Equal(TokenType.TokenPrimary, token.GetTokenType());
        Assert.NotEqual(TokenElevationType.Unknown, token.GetElevationType());
        Assert.NotNull(token.GetOwner());
        Assert.NotNull(token.GetMandatoryIntegrityLevel());
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void EnumerateGroupsContainsTheEveryoneGroup()
    {
        using var token = AccessToken.OpenCurrentProcessToken(TokenAccessLevels.Query);
        var groups = token.EnumerateGroups();

        Assert.NotNull(groups);
        Assert.Contains(groups, group => group.Sid == SecurityIdentifier.FromWellKnown(WellKnownSidType.WinWorldSid));
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void LinkedAccessTokenTest()
    {
        using var token = AccessToken.OpenCurrentProcessToken(TokenAccessLevels.Query);
        PrintToken(token);

        try
        {
            using var linkedToken = token.GetLinkedToken();
            PrintToken(linkedToken);
        }
        catch (Win32Exception) when (TestEnvironment.IsOnGitHubActions())
        {
        }
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void IsAdministratorTest()
    {
        _output.WriteLine(IsAdministrator().ToString());
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void IsAdministratorReturnsTrueWhenTheCurrentTokenHasTheAdministratorsGroupEnabled()
    {
        using var token = AccessToken.OpenCurrentProcessToken(TokenAccessLevels.Query);
        var adminSid = SecurityIdentifier.FromWellKnown(WellKnownSidType.WinBuiltinAdministratorsSid);
        var hasEnabledAdministratorsGroup = token.EnumerateGroups()
            .Any(group => group.Attributes.HasFlag(GroupSidAttributes.SE_GROUP_ENABLED) && group.Sid == adminSid);

        // Only meaningful when the test process is elevated; on a UAC-filtered token the group is deny-only
        if (hasEnabledAdministratorsGroup)
        {
            Assert.True(IsAdministrator());
        }
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void EnablePrivilegeThrowsWhenTheTokenDoesNotHoldThePrivilege()
    {
        const int ErrorNotAllAssigned = 1300;

        // SeTcbPrivilege is not granted to standard users nor to Administrators by default
        using var token = AccessToken.OpenCurrentProcessToken(TokenAccessLevels.Query | TokenAccessLevels.AdjustPrivileges);
        var exception = Assert.Throws<Win32Exception>(() => token.EnablePrivilege(Privileges.SE_TCB_NAME));

        Assert.Equal(ErrorNotAllAssigned, exception.NativeErrorCode);
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void DisposeCanBeCalledMoreThanOnce()
    {
        var token = AccessToken.OpenCurrentProcessToken(TokenAccessLevels.Query);
        token.Dispose();
        token.Dispose();
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void UsingADisposedTokenThrowsObjectDisposedException()
    {
        var token = AccessToken.OpenCurrentProcessToken(TokenAccessLevels.Query);
        token.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = token.IsRestricted());
        Assert.Throws<ObjectDisposedException>(() => _ = token.GetTokenType());
        Assert.Throws<ObjectDisposedException>(() => _ = token.GetOwner());
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void EnumeratePrivilegesReturnsNamesWithoutTerminatingNullCharacter()
    {
        using var token = AccessToken.OpenCurrentProcessToken(TokenAccessLevels.Query);
        var privileges = token.EnumeratePrivileges();

        Assert.NotNull(privileges);
        Assert.NotEmpty(privileges);
        foreach (var privilege in privileges)
        {
            Assert.DoesNotContain('\0', privilege.Name);
        }

        // SeChangeNotifyPrivilege is granted to Everyone by default, so the name must match the constant exactly
        Assert.Contains(privileges, privilege => string.Equals(privilege.Name, Privileges.SE_CHANGE_NOTIFY_NAME, StringComparison.Ordinal));
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void FromWellKnownTest()
    {
        _output.WriteLine("WellKnownSID " + SecurityIdentifier.FromWellKnown(WellKnownSidType.WinLowLabelSid));

        Assert.Equal("S-1-16-4096", SecurityIdentifier.FromWellKnown(WellKnownSidType.WinLowLabelSid).Sid);
        Assert.Equal("S-1-5-32-544", SecurityIdentifier.FromWellKnown(WellKnownSidType.WinBuiltinAdministratorsSid).Sid);
        Assert.Equal("S-1-1-0", SecurityIdentifier.FromWellKnown(WellKnownSidType.WinWorldSid).Sid);
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void FromWellKnownDocumentationExample()
    {
        #region SecurityIdentifier_FromWellKnown
        var adminSid = SecurityIdentifier.FromWellKnown(WellKnownSidType.WinBuiltinAdministratorsSid);
        var systemSid = SecurityIdentifier.FromWellKnown(WellKnownSidType.WinLocalSystemSid);
        Assert.Equal("S-1-5-32-544", adminSid.Sid);
        Assert.Equal("S-1-5-18", systemSid.Sid);
        #endregion
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void SecurityIdentifierDocumentationExample()
    {
        #region SecurityIdentifier_Usage
        // Get a well-known SID
        var adminSid = SecurityIdentifier.FromWellKnown(WellKnownSidType.WinBuiltinAdministratorsSid);
        Assert.Equal("S-1-5-32-544", adminSid.Sid);

        // The account name is localized: "BUILTIN\Administrators" on an English version of Windows
        Assert.NotEmpty(adminSid.FullName);

        // Compare SIDs
        using var token = AccessToken.OpenCurrentProcessToken(TokenAccessLevels.Query);
        var ownerSid = token.GetOwner();
        if (ownerSid == adminSid)
        {
            // The owner is the Administrators group, which is the case of an elevated process
        }
        #endregion
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void SecurityIdentifiersWithTheSameSidAreEqual()
    {
        var sid = SecurityIdentifier.FromWellKnown(WellKnownSidType.WinBuiltinAdministratorsSid);
        var other = SecurityIdentifier.FromWellKnown(WellKnownSidType.WinBuiltinAdministratorsSid);

        Assert.Equal(sid, other);
        Assert.True(sid == other);
        Assert.Equal(sid.GetHashCode(), other.GetHashCode());
        Assert.NotEqual(sid, SecurityIdentifier.FromWellKnown(WellKnownSidType.WinWorldSid));
    }

    private void PrintToken(AccessToken? token)
    {
        if (token is null)
            return;

        _output.WriteLine("Owner: " + token.GetOwner());
        _output.WriteLine("TokenType: " + token.GetTokenType());
        _output.WriteLine("ElevationType: " + token.GetElevationType());
        _output.WriteLine("IsElevatedToken: " + token.IsElevated());
        _output.WriteLine("IsRestricted: " + token.IsRestricted());
        _output.WriteLine("MandatoryIntegrityLevel: " + token.GetMandatoryIntegrityLevel()?.Sid);
        foreach (var group in token.EnumerateGroups())
        {
            _output.WriteLine($"Group: {group.Sid} ({group.Attributes})");
        }

        foreach (var group in token.EnumerateRestrictedSid())
        {
            _output.WriteLine($"Restricted Group: {group.Sid} ({group.Attributes})");
        }

        foreach (var privilege in token.EnumeratePrivileges())
        {
            _output.WriteLine($"Privilege: {privilege.Name} ({privilege.Attributes})");
        }
    }

    public static bool IsAdministrator()
    {
        using var token = AccessToken.OpenCurrentProcessToken(TokenAccessLevels.Query);
        if (token is null)
            return false;

        if (IsAdministrator(token))
            return true;

        if (token.GetElevationType() == TokenElevationType.Limited)
        {
            using var linkedToken = token.GetLinkedToken();
            return IsAdministrator(linkedToken);
        }

        return false;

        static bool IsAdministrator(AccessToken? accessToken)
        {
            if (accessToken is null)
                return false;

            var adminSid = SecurityIdentifier.FromWellKnown(WellKnownSidType.WinBuiltinAdministratorsSid);
            foreach (var group in accessToken.EnumerateGroups())
            {
                if (group.Attributes.HasFlag(GroupSidAttributes.SE_GROUP_ENABLED) && group.Sid == adminSid)
                    return true;
            }

            return false;
        }
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void IsLimitedTokenDocumentationExample()
    {
        #region AccessToken_IsLimitedToken
        // true when an administrator runs the process without elevation (User Account Control)
        var isLimited = AccessToken.IsLimitedToken();

        using var token = AccessToken.OpenCurrentProcessToken(TokenAccessLevels.Query);
        Assert.Equal(token.GetElevationType() == TokenElevationType.Limited, isLimited);
        #endregion
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void DuplicateDocumentationExample()
    {
        #region AccessToken_Duplicate
        // Duplicating a token requires the Duplicate access right
        using var token = AccessToken.OpenCurrentProcessToken(TokenAccessLevels.Query | TokenAccessLevels.Duplicate);

        using var impersonationToken = token.Duplicate(SecurityImpersonationLevel.SecurityImpersonation);
        Assert.Equal(TokenType.TokenImpersonation, impersonationToken.GetTokenType());
        #endregion
    }
}
