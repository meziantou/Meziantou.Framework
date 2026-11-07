namespace Meziantou.Framework.JavaScriptEngine.Compiler;

/// <summary>The runtime description of a declarative environment: its slots and the names of its bindings (for the lookups by name of eval and with).</summary>
internal sealed class ScopeInfo(ScopeKind kind, string?[] names, ScopeSlotFlags[] flags, bool isVarScope, bool isParameterVarScope = false)
{
    public ScopeKind Kind { get; } = kind;

    /// <summary>Gets the name of each slot (null for the hidden slots).</summary>
    public string?[] Names { get; } = names;

    public ScopeSlotFlags[] Flags { get; } = flags;

    public int SlotCount => Names.Length;

    /// <summary>Gets whether the environment is a variable environment (sloppy eval code adds its vars to it).</summary>
    public bool IsVarScope { get; } = isVarScope;

    /// <summary>Gets whether the environment is the parameter scope of a function whose parameters have expressions, used as the variable environment of the eval code of the parameters: the vars of the eval code cannot redeclare its bindings.</summary>
    public bool IsParameterVarScope { get; } = isParameterVarScope;

    public int IndexOf(string name)
    {
        for (var i = 0; i < Names.Length; i++)
        {
            if (string.Equals(Names[i], name, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }
}
