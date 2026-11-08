namespace Meziantou.Framework.Tds.Handler;

/// <summary>Describes a transaction manager request.</summary>
public enum TdsTransactionRequestType
{
    /// <summary>Begins a transaction (<c>SqlConnection.BeginTransaction</c>).</summary>
    Begin,

    /// <summary>Commits the transaction (<c>SqlTransaction.Commit</c>).</summary>
    Commit,

    /// <summary>
    /// Rolls back the transaction, or rolls back to a savepoint when <see cref="TdsTransactionContext.SavepointName"/>
    /// is set (<c>SqlTransaction.Rollback</c>).
    /// </summary>
    Rollback,

    /// <summary>Creates a savepoint (<c>SqlTransaction.Save</c>).</summary>
    Save,
}
