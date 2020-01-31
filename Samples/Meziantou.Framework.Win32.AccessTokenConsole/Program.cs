using System;

namespace Meziantou.Framework.Win32.AccessTokenConsoleTests
{
    internal static class Program
    {
        private static void Main()
        {
            using var token = AccessToken.OpenCurrentProcessToken(TokenAccessLevels.Query);
            PrintToken(token);

            using var linkedToken = token.GetLinkedToken();
            PrintToken(linkedToken);

            Console.WriteLine("WellKownSID " + SecurityIdentifier.FromWellKnown(WellKnownSidType.WinLowLabelSid));
        }

        private static void PrintToken(AccessToken token)
        {
            Console.WriteLine("Owner: " + token.GetOwner());
            Console.WriteLine("TokenType: " + token.GetTokenType());
            Console.WriteLine("ElevationType: " + token.GetElevationType());
            Console.WriteLine("IsElevatedToken: " + token.IsElevated());
            Console.WriteLine("IsRestricted: " + token.IsRestricted());
            Console.WriteLine("MandatoryIntegrityLevel: " + token.GetMandatoryIntegrityLevel()?.Sid);

            foreach (var group in token.EnumerateGroups())
            {
                Console.WriteLine($"Group: {group.Sid} ({group.Attributes})");
            }

            foreach (var group in token.EnumerateRestrictedSid())
            {
                Console.WriteLine($"Restricted Group: {group.Sid} ({group.Attributes})");
            }

            foreach (var privilege in token.EnumeratePrivileges())
            {
                Console.WriteLine($"Privilege: {privilege.Name} ({privilege.Attributes})");
            }
        }
    }
}
