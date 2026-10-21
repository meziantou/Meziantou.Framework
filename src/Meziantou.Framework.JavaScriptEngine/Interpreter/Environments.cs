using Meziantou.Framework.JavaScriptEngine.Compiler;

namespace Meziantou.Framework.JavaScriptEngine.Interpreter;

/// <summary>An environment record (https://tc39.es/ecma262/#sec-environment-records) at runtime.</summary>
internal abstract class Environment(Environment? outer) : JsCell(JsValueKind.Undefined)
{
    public Environment? Outer { get; } = outer;
}

/// <summary>A declarative environment: the slots of the bindings of a scope, and the vars added by sloppy eval code.</summary>
internal sealed class DeclarativeEnvironment : Environment
{
    public DeclarativeEnvironment(ScopeInfo info, Environment? outer)
        : base(outer)
    {
        Info = info;
        var slots = new JsValue[info.SlotCount];
        var flags = info.Flags;
        for (var i = 0; i < slots.Length; i++)
        {
            if ((flags[i] & ScopeSlotFlags.Uninitialized) != 0)
            {
                slots[i] = JsValue.Hole;
            }
        }

        Slots = slots;
    }

    private DeclarativeEnvironment(DeclarativeEnvironment source)
        : base(source.Outer)
    {
        Info = source.Info;
        Slots = (JsValue[])source.Slots.Clone();
    }

    public ScopeInfo Info { get; }

    public JsValue[] Slots { get; }

    /// <summary>Gets the vars created by sloppy eval code in this variable environment (they can be deleted).</summary>
    public Dictionary<string, JsValue>? EvalVars { get; private set; }

    /// <summary>CreatePerIterationEnvironment: a copy of the environment with the same values.</summary>
    public DeclarativeEnvironment Copy() => new(this);

    public bool HasEvalVar(string name) => EvalVars is not null && EvalVars.ContainsKey(name);

    public void SetEvalVar(string name, JsValue value) => (EvalVars ??= new(StringComparer.Ordinal))[name] = value;
}

/// <summary>An object environment: the binding object of a with statement.</summary>
internal sealed class ObjectEnvironment(JsObject bindingObject, Environment? outer) : Environment(outer)
{
    public JsObject BindingObject { get; } = bindingObject;
}

/// <summary>A binding of the declarative record of a global environment (let, const and class of the scripts).</summary>
internal sealed class GlobalLexicalBinding
{
    public JsValue Value { get; set; } = JsValue.Hole;

    public bool IsConstant { get; init; }
}

/// <summary>The global environment of a realm: the global object and the lexical bindings of the scripts.</summary>
internal sealed class GlobalEnvironment(JsRealm realm) : Environment(outer: null)
{
    public JsRealm Realm { get; } = realm;

    public JsObject GlobalObject => Realm.GlobalObject;

    public Dictionary<string, GlobalLexicalBinding> LexicalBindings { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets or sets the number of scripts that declared lexical bindings (the inline caches of the properties of the global object check it).</summary>
    public int LexicalVersion { get; set; }
}
