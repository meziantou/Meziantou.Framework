using System.ComponentModel;
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
    public void AccessTokenTest()
    {
        using var token = AccessToken.OpenCurrentProcessToken(TokenAccessLevels.Query);
        PrintToken(token);
        var owner = token.GetOwner();
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
    public void EnablePrivilegeThrowsWhenTheTokenDoesNotHoldThePrivilege()
    {
        const int ErrorNotAllAssigned = 1300;

        // SeTcbPrivilege is not granted to standard users nor to Administrators by default
        using var token = AccessToken.OpenCurrentProcessToken(TokenAccessLevels.Query | TokenAccessLevels.AdjustPrivileges);
        var exception = Assert.Throws<Win32Exception>(() => token.EnablePrivilege(Privileges.SE_TCB_NAME));

        Assert.Equal(ErrorNotAllAssigned, exception.NativeErrorCode);
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
        foreach (var group in token.EnumerateGroups() ?? [])
        {
            _output.WriteLine($"Group: {group.Sid} ({group.Attributes})");
        }

        foreach (var group in token.EnumerateRestrictedSid() ?? [])
        {
            _output.WriteLine($"Restricted Group: {group.Sid} ({group.Attributes})");
        }

        foreach (var privilege in token.EnumeratePrivileges() ?? [])
        {
            _output.WriteLine($"Privilege: {privilege.Name} ({privilege.Attributes})");
        }
    }

    public static bool IsAdministrator()
    {
        using var token = AccessToken.OpenCurrentProcessToken(TokenAccessLevels.Query);
        if (token is null)
            return false;

        if (!IsAdministrator(token) && token.GetElevationType() == TokenElevationType.Limited)
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
            foreach (var group in accessToken.EnumerateGroups() ?? [])
            {
                if (group.Attributes.HasFlag(GroupSidAttributes.SE_GROUP_ENABLED) && group.Sid == adminSid)
                    return true;
            }

            return false;
        }
    }
}
