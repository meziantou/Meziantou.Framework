namespace Meziantou.Framework.NodeJs.Internal;

/// <summary>How the Node.js process returns the result of a call.</summary>
internal enum ResultKind
{
    /// <summary>The result is serialized as JSON.</summary>
    Json,

    /// <summary>The result is ignored.</summary>
    Void,

    /// <summary>The result is kept in the Node.js process, and its reference is returned.</summary>
    Reference,
}
