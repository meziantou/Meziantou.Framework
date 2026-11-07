namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>Identifies the kind of a <see cref="PublicApiMethod"/>.</summary>
public enum PublicApiMethodKind
{
    Ordinary,
    Constructor,
    Destructor,

    /// <summary>A user-defined operator, such as <c>op_Addition</c>.</summary>
    Operator,

    /// <summary>A user-defined conversion operator: <c>op_Implicit</c>, <c>op_Explicit</c> or <c>op_CheckedExplicit</c>.</summary>
    Conversion,
    PropertyGet,
    PropertySet,
    EventAdd,
    EventRemove,
    EventRaise,

    /// <summary>The <c>Invoke</c> method of a delegate type.</summary>
    DelegateInvoke,
}
