using Meziantou.Framework.Tds.Handler;

namespace Meziantou.Framework.Tds.Protocol;

internal sealed class TdsRpcRequest
{
    public string? ProcedureName { get; init; }

    public required List<TdsQueryParameter> Parameters { get; init; }
}
