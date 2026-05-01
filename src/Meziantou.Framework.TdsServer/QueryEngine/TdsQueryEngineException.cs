using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.Tds.QueryEngine;

[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Internal exception used only to carry query-engine error messages to TDS error responses.")]
[SuppressMessage("Design", "CA1064:Exceptions should be public", Justification = "Internal exception used only within the query engine.")]
internal sealed class TdsQueryEngineException : Exception
{
    public TdsQueryEngineException(string message)
        : base(message)
    {
    }
}
