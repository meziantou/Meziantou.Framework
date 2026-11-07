namespace Meziantou.Framework.PostgreSql.Handler;

/// <summary>Describes the transaction state reported to the client after a command completes.</summary>
public enum PostgreSqlTransactionStatus
{
    /// <summary>Not inside a transaction block.</summary>
    Idle,

    /// <summary>Inside a transaction block.</summary>
    InTransaction,

    /// <summary>Inside a failed transaction block; commands are rejected until the block ends.</summary>
    Failed,
}
