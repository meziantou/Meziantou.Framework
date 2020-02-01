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
            Console.WriteLine("IsAdministrator " + IsAdministrator());
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
}
