using System.ComponentModel;
using TestUtilities;
using Xunit.Abstractions;

namespace Meziantou.Framework.Win32.Tests;

public class AccessTokenTests
{
    private readonly ITestOutputHelper _output;

    public AccessTokenTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [RunIfFact(FactOperatingSystem.Windows)]
    public void AccessTokenTest()
    {
        using var token = AccessToken.OpenCurrentProcessToken(TokenAccessLevels.Query);
        PrintToken(token);
    }

    [RunIfFact(FactOperatingSystem.Windows)]
    public void LinkedAccessTokenTest()
    {
        using var token = AccessToken.OpenCurrentProcessToken(TokenAccessLevels.Query);
        PrintToken(token);

        try
        {
            using var linkedToken = token.GetLinkedToken();
            PrintToken(linkedToken);
        }
        catch (Win32Exception) when (RunIfFactAttribute.IsOnGitHubActions())
        {
        }
    }

    [RunIfFact(FactOperatingSystem.Windows)]
    public void IsAdministratorTest()
    {
        _output.WriteLine(IsAdministrator().ToString());
    }

    [RunIfFact(FactOperatingSystem.Windows)]
    public void FromWellKnownTest()
    {
        _output.WriteLine("WellKownSID " + SecurityIdentifier.FromWellKnown(WellKnownSidType.WinLowLabelSid));
    }

    private void PrintToken(AccessToken token)
    {
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
        if (!IsAdministrator(token) && token.GetElevationType() == TokenElevationType.Limited)
        {
            using var linkedToken = token.GetLinkedToken();
            return IsAdministrator(linkedToken);
        }

        return false;

        static bool IsAdministrator(AccessToken accessToken)
        {
            var adminSid = SecurityIdentifier.FromWellKnown(WellKnownSidType.WinBuiltinAdministratorsSid);
            foreach (var group in accessToken.EnumerateGroups())
            {
                if (group.Attributes.HasFlag(GroupSidAttributes.SE_GROUP_ENABLED) && group.Sid == adminSid)
                    return true;
            }

            return false;
        }
    }
}
