using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.Tds.QueryEngine;

[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Internal exception used only to carry query-engine authorization errors to TDS error responses.")]
[SuppressMessage("Design", "CA1064:Exceptions should be public", Justification = "Internal exception used only within the query engine.")]
internal sealed class TdsQueryAuthorizationException : Exception
{
    public TdsQueryAuthorizationException(TdsQueryEngineResourceKind resourceKind, string resourceName)
        : base($"Not authorized to access {resourceKind} '{resourceName}'.")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);

        ResourceKind = resourceKind;
        ResourceName = resourceName;
    }

    public TdsQueryEngineResourceKind ResourceKind { get; }

    public string ResourceName { get; }
}
