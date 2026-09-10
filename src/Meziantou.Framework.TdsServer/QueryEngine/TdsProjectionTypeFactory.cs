namespace Meziantou.Framework.Tds.QueryEngine;

internal static class TdsProjectionTypeFactory
{
    /// <summary>
    /// Emitted types live in a non-collectible dynamic assembly, so they can never be reclaimed, and column
    /// aliases come from client SQL -- which makes the number of distinct shapes client-controlled. Cap it so a
    /// client cannot grow the process without bound by varying aliases across queries.
    /// </summary>
    private const int MaxCachedTypes = 1024;

    private static readonly TdsProjectionTypeCache Shared = new(MaxCachedTypes);

    public static Type GetProjectionType(IReadOnlyList<TdsProjectionMember> members)
    {
        return Shared.GetProjectionType(members);
    }

    public static Type GetCarrierType(IReadOnlyList<TdsProjectionMember> members)
    {
        return Shared.GetCarrierType(members);
    }
}
