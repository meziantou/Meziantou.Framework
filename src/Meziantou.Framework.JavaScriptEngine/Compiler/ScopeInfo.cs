namespace Meziantou.Framework.JavaScriptEngine.Compiler;

/// <summary>The runtime description of a declarative environment: its slots and the names of its bindings (for the lookups by name of eval and with).</summary>
internal sealed class ScopeInfo(ScopeKind kind, string?[] names, ScopeSlotFlags[] flags, bool isVarScope)
{
    public ScopeKind Kind { get; } = kind;

    /// <summary>Gets the name of each slot (null for the hidden slots).</summary>
    public string?[] Names { get; } = names;

    public ScopeSlotFlags[] Flags { get; } = flags;

    public int SlotCount => Names.Length;

    /// <summary>Gets whether the environment is a variable environment (sloppy eval code adds its vars to it).</summary>
    public bool IsVarScope { get; } = isVarScope;

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
