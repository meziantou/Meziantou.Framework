namespace Meziantou.Framework.ChromiumTracing;

/// <summary>Specifies how a flow event connects to other events.</summary>
public enum BindingPoint
{
    /// <summary>The flow event connects to the next slice.</summary>
    NextSlice,

    /// <summary>The flow event connects to the enclosing slice.</summary>
    EnclosingSlice,
}
