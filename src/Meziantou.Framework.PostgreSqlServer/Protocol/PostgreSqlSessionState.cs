using System.Net;

namespace Meziantou.Framework.PostgreSql.Protocol;

/// <summary>Holds the per-connection state of an authenticated session.</summary>
internal sealed class PostgreSqlSessionState
{
    public PostgreSqlSessionState(EndPoint remoteEndPoint, IReadOnlyDictionary<string, string> startupParameters, PostgreSqlBackendSession? backendSession)
    {
        RemoteEndPoint = remoteEndPoint;
        StartupParameters = startupParameters;
        BackendSession = backendSession;
    }

    public EndPoint RemoteEndPoint { get; }

    public IReadOnlyDictionary<string, string> StartupParameters { get; }

    public PostgreSqlBackendSession? BackendSession { get; }

    public Dictionary<string, PostgreSqlStatement> PreparedStatements { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, PostgreSqlPortal> Portals { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets or sets a value indicating whether messages are being skipped until the next Sync.</summary>
    public bool InErrorState { get; set; }

    /// <summary>Gets or sets the transaction status reported by ReadyForQuery.</summary>
    public byte TransactionStatus { get; set; } = PostgreSqlConstants.TransactionStatus.Idle;
}
