namespace Meziantou.Framework.Tds.QueryEngine;

[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Internal exception used only to carry query-engine error messages to TDS error responses.")]
[SuppressMessage("Design", "CA1064:Exceptions should be public", Justification = "Internal exception used only within the query engine.")]
internal sealed class TdsQueryEngineException : Exception
{
    public TdsQueryEngineException(string message)
        : this(message, errorNumber: 50004)
    {
    }

    public TdsQueryEngineException(string message, uint errorNumber)
        : base(message)
    {
        ErrorNumber = errorNumber;
    }

    /// <summary>The number of the error reported to the client: the SQL Server one when the engine models it, 50004 otherwise.</summary>
    public uint ErrorNumber { get; }
}
