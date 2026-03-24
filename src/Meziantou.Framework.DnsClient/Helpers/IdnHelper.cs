namespace Meziantou.Framework.DnsClient.Helpers;

internal static class IdnHelper
{
    private static readonly IdnMapping IdnMapping = new();

    public static string ToAscii(string domainName)
    {
        ArgumentNullException.ThrowIfNull(domainName);

        if (IsAscii(domainName))
            return domainName;

        return IdnMapping.GetAscii(domainName);
    }

    private static bool IsAscii(string value)
    {
        foreach (var c in value)
        {
            if (c > 127)
                return false;
        }

        return true;
    }
}
