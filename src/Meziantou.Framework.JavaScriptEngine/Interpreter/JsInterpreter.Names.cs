using Meziantou.Framework.JavaScriptEngine.Compiler;

namespace Meziantou.Framework.JavaScriptEngine.Interpreter;

internal static partial class JsInterpreter
{
    // ------------------------------------------------------------------------------------------------------------------
    // Global bindings
    // ------------------------------------------------------------------------------------------------------------------

    private static JsValue LoadGlobal(JsRealm realm, in PropertyKey key, bool throwIfMissing)
    {
        var environment = realm.GlobalEnvironment;
        if (key.IsString && environment.LexicalBindings.TryGetValue(key.AsString(), out var binding))
        {
            if (binding.Value.IsHole)
            {
                ThrowTdz(key);
            }

            return binding.Value;
        }

        var global = realm.GlobalObject;
        if (!global.HasProperty(key))
        {
            if (throwIfMissing)
            {
                ThrowHelper.ThrowReferenceError($"{key} is not defined");
            }

            return default;
        }

        return global.Get(key, realm.GlobalObject);
    }

    private static void StoreGlobal(JsRealm realm, in PropertyKey key, JsValue value, bool strict)
    {
        var environment = realm.GlobalEnvironment;
        if (key.IsString && environment.LexicalBindings.TryGetValue(key.AsString(), out var binding))
        {
            if (binding.Value.IsHole)
            {
                ThrowTdz(key);
            }

            if (binding.IsConstant)
            {
                ThrowHelper.ThrowTypeError("Assignment to constant variable.");
            }

            binding.Value = value;
            return;
        }

        var global = realm.GlobalObject;
        if (strict && !global.HasProperty(key))
        {
            ThrowHelper.ThrowReferenceError($"{key} is not defined");
        }

        if (!global.Set(key, value, global) && strict)
        {
            ThrowHelper.ThrowTypeError($"Cannot assign to read only property '{key}' of object");
        }
    }

    private static void InitializeGlobalLexical(JsRealm realm, in PropertyKey key, JsValue value)
    {
        realm.GlobalEnvironment.LexicalBindings[key.AsString()].Value = value;
    }

    /// <summary>CreateGlobalFunctionBinding(N, V, D)</summary>
    private static void InitializeGlobalFunction(JsRealm realm, in PropertyKey key, JsValue value, bool deletable = false)
    {
        var global = realm.GlobalObject;
        var hasExisting = global.GetOwnProperty(key, out var existing);
        var descriptor = !hasExisting || existing.Configurable
            ? PropertyDescriptor.Data(value, deletable ? PropertyFlags.Default : PropertyFlags.Writable | PropertyFlags.Enumerable)
            : PropertyDescriptor.ValueOnly(value);
        global.DefinePropertyOrThrow(key, descriptor);
        global.Set(key, value, throwOnFailure: true);
    }

    /// <summary>GlobalDeclarationInstantiation(script, env) (https://tc39.es/ecma262/#sec-globaldeclarationinstantiation).</summary>
    private static void GlobalDeclarationInstantiation(JsRealm realm, GlobalDeclarations declarations)
    {
        var environment = realm.GlobalEnvironment;
        var global = realm.GlobalObject;
        foreach (var (name, _) in declarations.LexicalNames)
        {
            if (environment.LexicalBindings.ContainsKey(name))
            {
                ThrowHelper.ThrowSyntaxError($"Identifier '{name}' has already been declared");
            }

            // HasRestrictedGlobalProperty: an own non-configurable property of the global object
            if (global.GetOwnProperty(name, out var existing) && !existing.Configurable)
            {
                ThrowHelper.ThrowSyntaxError($"Identifier '{name}' has already been declared");
            }
        }

        foreach (var name in declarations.VarNames.Concat(declarations.FunctionNames))
        {
            if (environment.LexicalBindings.ContainsKey(name))
            {
                ThrowHelper.ThrowSyntaxError($"Identifier '{name}' has already been declared");
            }
        }

        foreach (var name in declarations.FunctionNames)
        {
            if (!CanDeclareGlobalFunction(global, name))
            {
                ThrowHelper.ThrowTypeError($"Cannot declare the global function '{name}'");
            }
        }

        foreach (var name in declarations.VarNames)
        {
            if (!CanDeclareGlobalVar(global, name))
            {
                ThrowHelper.ThrowTypeError($"Cannot declare the global variable '{name}'");
            }
        }

        foreach (var (name, isConstant) in declarations.LexicalNames)
        {
            environment.LexicalBindings[name] = new GlobalLexicalBinding { IsConstant = isConstant };
        }

        if (declarations.LexicalNames.Length > 0)
        {
            // A lexical binding shadows the property of the global object that an inline cache may have remembered
            environment.LexicalVersion++;
        }

        foreach (var name in declarations.VarNames)
        {
            CreateGlobalVarBinding(realm, name, deletable: false);
        }
    }

    private static bool CanDeclareGlobalFunction(JsObject global, string name)
    {
        if (!global.GetOwnProperty(name, out var existing))
            return global.IsExtensible();

        if (existing.Configurable)
            return true;

        return existing.IsDataDescriptor && existing.Writable && existing.Enumerable;
    }

    private static bool CanDeclareGlobalVar(JsObject global, string name) => global.HasOwnProperty(name) || global.IsExtensible();

    /// <summary>CreateGlobalVarBinding(N, D)</summary>
    private static void CreateGlobalVarBinding(JsRealm realm, string name, bool deletable)
    {
        var global = realm.GlobalObject;
        if (!global.HasOwnProperty(name) && global.IsExtensible())
        {
            global.DefinePropertyOrThrow(name, PropertyDescriptor.Data(JsValue.Undefined, deletable ? PropertyFlags.Default : PropertyFlags.Writable | PropertyFlags.Enumerable));
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Lookups by name (with statements, eval code)
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>ResolveBinding and GetValue: the value of a name in the environments of the frame.</summary>
    private static JsValue LoadName(Frame frame, in PropertyKey key, bool throwIfMissing, out JsValue thisValue)
    {
        thisValue = default;
        var name = key.AsString();
        for (var environment = frame.Environment; environment is not null; environment = environment.Outer)
        {
            switch (environment)
            {
                case DeclarativeEnvironment declarative:
                    {
                        var index = declarative.Info.IndexOf(name);
                        if (index >= 0)
                        {
                            var value = ImportBindingCell.Read(declarative.Slots[index], key);
                            if (value.IsHole)
                            {
                                ThrowTdz(key);
                            }

                            return value;
                        }

                        if (declarative.EvalVars is { } evalVars && evalVars.TryGetValue(name, out var evalValue))
                            return evalValue;

                        break;
                    }

                case ObjectEnvironment objectEnvironment:
                    if (HasWithBinding(objectEnvironment, key))
                    {
                        thisValue = objectEnvironment.BindingObject;
                        var bindingObject = objectEnvironment.BindingObject;
                        if (!bindingObject.HasProperty(key))
                        {
                            if (frame.IsStrict)
                            {
                                ThrowHelper.ThrowReferenceError($"{key} is not defined");
                            }

                            return default;
                        }

                        return bindingObject.Get(key, bindingObject);
                    }

                    break;
                case GlobalEnvironment:
                    if (name == "this")
                        return frame.Realm.GlobalThis;

                    if (name is "new.target" or "%home" or "%func")
                        return default;

                    return LoadGlobal(frame.Realm, key, throwIfMissing);
            }
        }

        return LoadGlobal(frame.Realm, key, throwIfMissing);
    }

    /// <summary>HasBinding of the object environment of a with statement: the property exists and is not in @@unscopables.</summary>
    private static bool HasWithBinding(ObjectEnvironment environment, in PropertyKey key)
    {
        var bindingObject = environment.BindingObject;
        if (!bindingObject.HasProperty(key))
            return false;

        var unscopables = bindingObject.Get(new PropertyKey(WellKnownSymbols.Unscopables), bindingObject);
        if (unscopables.Cell is JsObject unscopablesObject)
            return !JsOperations.ToBoolean(unscopablesObject.Get(key, unscopablesObject));

        return true;
    }

    private static void StoreName(Frame frame, in PropertyKey key, JsValue value, bool strict)
    {
        var name = key.AsString();
        for (var environment = frame.Environment; environment is not null; environment = environment.Outer)
        {
            switch (environment)
            {
                case DeclarativeEnvironment declarative:
                    {
                        var index = declarative.Info.IndexOf(name);
                        if (index >= 0)
                        {
                            var flags = declarative.Info.Flags[index];
                            if (declarative.Slots[index].IsHole)
                            {
                                ThrowTdz(key);
                            }

                            if ((flags & ScopeSlotFlags.FunctionName) != 0)
                            {
                                if (strict)
                                {
                                    ThrowHelper.ThrowTypeError("Assignment to constant variable.");
                                }

                                return;
                            }

                            if ((flags & ScopeSlotFlags.Immutable) != 0)
                            {
                                ThrowHelper.ThrowTypeError("Assignment to constant variable.");
                            }

                            declarative.Slots[index] = value;
                            return;
                        }

                        if (declarative.EvalVars is { } evalVars && evalVars.ContainsKey(name))
                        {
                            evalVars[name] = value;
                            return;
                        }

                        break;
                    }

                case ObjectEnvironment objectEnvironment:
                    if (HasWithBinding(objectEnvironment, key))
                    {
                        // SetMutableBinding of an object environment
                        var bindingObject = objectEnvironment.BindingObject;
                        var stillExists = bindingObject.HasProperty(key);
                        if (!stillExists && strict)
                        {
                            ThrowHelper.ThrowReferenceError($"{key} is not defined");
                        }

                        if (!bindingObject.Set(key, value, bindingObject) && strict)
                        {
                            ThrowHelper.ThrowTypeError($"Cannot assign to read only property '{key}' of object");
                        }

                        return;
                    }

                    break;
                case GlobalEnvironment:
                    StoreGlobal(frame.Realm, key, value, strict);
                    return;
            }
        }

        StoreGlobal(frame.Realm, key, value, strict);
    }

    /// <summary>delete identifier (sloppy code).</summary>
    private static JsValue DeleteName(Frame frame, in PropertyKey key)
    {
        var name = key.AsString();
        for (var environment = frame.Environment; environment is not null; environment = environment.Outer)
        {
            switch (environment)
            {
                case DeclarativeEnvironment declarative:
                    if (declarative.Info.IndexOf(name) >= 0)
                        return JsValue.False;

                    if (declarative.EvalVars is { } evalVars && evalVars.Remove(name))
                        return JsValue.True;

                    break;
                case ObjectEnvironment objectEnvironment:
                    if (HasWithBinding(objectEnvironment, key))
                        return JsValue.FromBoolean(objectEnvironment.BindingObject.Delete(key));

                    break;
                case GlobalEnvironment global:
                    if (global.LexicalBindings.ContainsKey(name))
                        return JsValue.False;

                    if (global.GlobalObject.HasOwnProperty(key))
                        return JsValue.FromBoolean(global.GlobalObject.Delete(key));

                    return JsValue.True;
            }
        }

        return JsValue.True;
    }

    /// <summary>
    /// BindThisValue of the this environment of eval code (super() in a direct eval): the binding named "this" of the
    /// enclosing derived constructor. It is read without the TDZ check.
    /// </summary>
    private static void BindThisName(Frame frame, JsValue value)
    {
        for (var environment = frame.Environment; environment is not null; environment = environment.Outer)
        {
            if (environment is DeclarativeEnvironment declarative && declarative.Info.IndexOf("this") is var index and >= 0)
            {
                if (!declarative.Slots[index].IsHole)
                {
                    ThrowHelper.ThrowReferenceError("Super constructor may only be called once");
                }

                declarative.Slots[index] = value;
                return;
            }
        }

        throw new InvalidOperationException("super() in eval code without the this binding of a derived constructor");
    }

    /// <summary>Initializes a binding found by name: the function declarations of sloppy eval code, the this binding initialized by super() in eval code.</summary>
    private static void InitializeName(Frame frame, in PropertyKey key, JsValue value)
    {
        var name = key.AsString();
        for (var environment = frame.Environment; environment is not null; environment = environment.Outer)
        {
            switch (environment)
            {
                case DeclarativeEnvironment declarative:
                    {
                        var index = declarative.Info.IndexOf(name);
                        if (index >= 0)
                        {
                            declarative.Slots[index] = value;
                            return;
                        }

                        if (declarative.EvalVars is { } evalVars && evalVars.ContainsKey(name))
                        {
                            evalVars[name] = value;
                            return;
                        }

                        if (declarative.Info.IsVarScope && declarative.Info.Kind != ScopeKind.Eval)
                        {
                            declarative.SetEvalVar(name, value);
                            return;
                        }

                        break;
                    }

                case GlobalEnvironment:
                    InitializeGlobalFunction(frame.Realm, key, value, deletable: true);
                    return;
            }
        }
    }

    /// <summary>EvalDeclarationInstantiation of sloppy eval code (https://tc39.es/ecma262/#sec-evaldeclarationinstantiation).</summary>
    private static void EvalDeclarationInstantiation(Frame frame, EvalDeclarations declarations)
    {
        // The variable environment: the nearest var scope (function), or the global environment
        Environment? varEnvironment = null;
        for (var environment = frame.Environment; environment is not null; environment = environment.Outer)
        {
            if (environment is GlobalEnvironment || environment is DeclarativeEnvironment { Info: { IsVarScope: true, Kind: not ScopeKind.Eval } })
            {
                varEnvironment = environment;
                break;
            }
        }

        varEnvironment ??= frame.Realm.GlobalEnvironment;
        var names = declarations.VarNames.Concat(declarations.FunctionNames).ToList();
        if (varEnvironment is GlobalEnvironment globalEnvironment)
        {
            foreach (var name in names)
            {
                if (globalEnvironment.LexicalBindings.ContainsKey(name))
                {
                    ThrowHelper.ThrowSyntaxError($"Identifier '{name}' has already been declared");
                }
            }
        }

        // A var of the eval code cannot be declared over a lexical binding of the scopes between the eval and its variable
        // environment (the variable environment of the eval code of parameters is outside the parameters)
        var lastEnvironment = varEnvironment is DeclarativeEnvironment { Info.IsParameterVarScope: true } ? varEnvironment.Outer : varEnvironment;
        for (var environment = frame.Environment; environment is not null && environment != lastEnvironment; environment = environment.Outer)
        {
            if (environment is DeclarativeEnvironment declarative && declarative.Info.Kind is not ScopeKind.Eval and not ScopeKind.Catch)
            {
                foreach (var name in names)
                {
                    var index = declarative.Info.IndexOf(name);
                    if (index >= 0 && name is not "this" and not "new.target" and not "%home" and not "%func")
                    {
                        ThrowHelper.ThrowSyntaxError($"Identifier '{name}' has already been declared");
                    }
                }
            }
        }

        if (varEnvironment is GlobalEnvironment)
        {
            var global = frame.Realm.GlobalObject;
            foreach (var name in declarations.FunctionNames)
            {
                if (!CanDeclareGlobalFunction(global, name))
                {
                    ThrowHelper.ThrowTypeError($"Cannot declare the global function '{name}'");
                }
            }

            foreach (var name in declarations.VarNames)
            {
                if (!CanDeclareGlobalVar(global, name))
                {
                    ThrowHelper.ThrowTypeError($"Cannot declare the global variable '{name}'");
                }
            }

            foreach (var name in declarations.VarNames)
            {
                CreateGlobalVarBinding(frame.Realm, name, deletable: true);
            }

            return;
        }

        var varScope = (DeclarativeEnvironment)varEnvironment;
        foreach (var name in declarations.VarNames)
        {
            if (varScope.Info.IndexOf(name) < 0 && !varScope.HasEvalVar(name))
            {
                varScope.SetEvalVar(name, JsValue.Undefined);
            }
        }
    }

    /// <summary>
    /// ResolveBinding(name): the environment that has the binding, or undefined when the reference is unresolvable. In
    /// sloppy code, the global environment is the result without looking at its bindings: PutValue creates the missing
    /// global, and the binding is looked up when the reference is used.
    /// </summary>
    private static JsValue ResolveName(Frame frame, in PropertyKey key)
    {
        var name = key.AsString();
        for (var environment = frame.Environment; environment is not null; environment = environment.Outer)
        {
            switch (environment)
            {
                case DeclarativeEnvironment declarative:
                    if (declarative.Info.IndexOf(name) >= 0 || declarative.HasEvalVar(name))
                        return new JsValue(declarative);

                    break;
                case ObjectEnvironment objectEnvironment:
                    if (HasWithBinding(objectEnvironment, key))
                        return new JsValue(objectEnvironment);

                    break;
                case GlobalEnvironment:
                    return frame.IsStrict ? ResolveGlobalBinding(frame.Realm, key) : new JsValue(frame.Realm.GlobalEnvironment);
            }
        }

        return frame.IsStrict ? ResolveGlobalBinding(frame.Realm, key) : new JsValue(frame.Realm.GlobalEnvironment);
    }

    /// <summary>HasBinding of the global environment: the global environment when it has the binding, undefined when the reference is unresolvable.</summary>
    private static JsValue ResolveGlobalBinding(JsRealm realm, in PropertyKey key)
    {
        var environment = realm.GlobalEnvironment;
        if ((key.IsString && environment.LexicalBindings.ContainsKey(key.AsString())) || realm.GlobalObject.HasProperty(key))
            return new JsValue(environment);

        return default;
    }

    private static JsValue LoadResolved(Frame frame, JsValue reference, in PropertyKey key)
    {
        switch (reference.Cell)
        {
            case DeclarativeEnvironment declarative:
                {
                    var index = declarative.Info.IndexOf(key.AsString());
                    if (index < 0)
                        return declarative.EvalVars!.GetValueOrDefault(key.AsString());

                    var value = ImportBindingCell.Read(declarative.Slots[index], key);
                    if (value.IsHole)
                    {
                        ThrowTdz(key);
                    }

                    return value;
                }

            case ObjectEnvironment objectEnvironment:
                {
                    var bindingObject = objectEnvironment.BindingObject;
                    if (!bindingObject.HasProperty(key))
                    {
                        if (frame.IsStrict)
                        {
                            ThrowHelper.ThrowReferenceError($"{key} is not defined");
                        }

                        return default;
                    }

                    return bindingObject.Get(key, bindingObject);
                }

            default:
                return LoadGlobal(frame.Realm, key, throwIfMissing: true);
        }
    }

    private static void StoreResolved(Frame frame, JsValue reference, in PropertyKey key, JsValue value, bool strict)
    {
        switch (reference.Cell)
        {
            case DeclarativeEnvironment declarative:
                {
                    var name = key.AsString();
                    var index = declarative.Info.IndexOf(name);
                    if (index < 0)
                    {
                        declarative.SetEvalVar(name, value);
                        return;
                    }

                    var flags = declarative.Info.Flags[index];
                    if (declarative.Slots[index].IsHole)
                    {
                        ThrowTdz(key);
                    }

                    if ((flags & ScopeSlotFlags.FunctionName) != 0)
                    {
                        if (strict)
                        {
                            ThrowHelper.ThrowTypeError("Assignment to constant variable.");
                        }

                        return;
                    }

                    if ((flags & ScopeSlotFlags.Immutable) != 0)
                    {
                        ThrowHelper.ThrowTypeError("Assignment to constant variable.");
                    }

                    declarative.Slots[index] = value;
                    return;
                }

            case ObjectEnvironment objectEnvironment:
                {
                    var bindingObject = objectEnvironment.BindingObject;
                    var stillExists = bindingObject.HasProperty(key);
                    if (!stillExists && strict)
                    {
                        ThrowHelper.ThrowReferenceError($"{key} is not defined");
                    }

                    if (!bindingObject.Set(key, value, bindingObject) && strict)
                    {
                        ThrowHelper.ThrowTypeError($"Cannot assign to read only property '{key}' of object");
                    }

                    return;
                }

            case null when strict:
                // An unresolvable reference: PutValue throws, even when the evaluation of the value created the binding
                ThrowHelper.ThrowReferenceError($"{key} is not defined");
                return;

            default:
                StoreGlobal(frame.Realm, key, value, strict);
                return;
        }
    }

    /// <summary>Sets a binding of the variable environment (the nearest var scope of a function, or the global object).</summary>
    private static void SetVarBinding(Frame frame, in PropertyKey key, JsValue value)
    {
        var name = key.AsString();
        for (var environment = frame.Environment; environment is not null; environment = environment.Outer)
        {
            if (environment is DeclarativeEnvironment { Info: { IsVarScope: true, Kind: not ScopeKind.Eval } } declarative)
            {
                var index = declarative.Info.IndexOf(name);
                if (index >= 0)
                {
                    declarative.Slots[index] = value;
                }
                else
                {
                    declarative.SetEvalVar(name, value);
                }

                return;
            }

            if (environment is GlobalEnvironment)
                break;
        }

        frame.Realm.GlobalObject.Set(key, value, throwOnFailure: false);
    }
}
