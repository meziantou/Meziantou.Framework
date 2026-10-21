using Meziantou.Framework.JavaScriptEngine.Ast;

namespace Meziantou.Framework.JavaScriptEngine.Compiler;

internal sealed partial class ScopeAnalyzer
{
    private readonly List<(Node Node, Scope Scope, VariableKind Kind)> _specials = [];

    /// <summary>Gets the resolution of this, new.target, the home object (super) and the active function (super()).</summary>
    public Dictionary<(Node Node, VariableKind Kind), SpecialReference> SpecialReferences { get; } = [];

    private void RecordSpecialReference(Node node, Scope scope, VariableKind kind) => _specials.Add((node, scope, kind));

    public SpecialReference GetSpecialReference(Node node, VariableKind kind) => SpecialReferences[(node, kind)];

    public static string GetSpecialName(VariableKind kind)
    {
        return kind switch
        {
            VariableKind.This => "this",
            VariableKind.NewTarget => "new.target",
            VariableKind.HomeObject => "%home",
            VariableKind.ActiveFunction => "%func",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a special binding"),
        };
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Pass 2
    // ------------------------------------------------------------------------------------------------------------------

    private void ResolveAll()
    {
        // The functions that contain a direct eval expose their special bindings to the eval code
        foreach (var scope in _allScopes)
        {
            if (scope.Function.HasDirectEval && scope == scope.Function.Scope)
            {
                var owner = GetThisFunction(scope.Function);
                if (owner.Kind is not FunctionInfoKind.Script and not FunctionInfoKind.Module and not FunctionInfoKind.Eval)
                {
                    foreach (var kind in (ReadOnlySpan<VariableKind>)[VariableKind.This, VariableKind.NewTarget, VariableKind.HomeObject, VariableKind.ActiveFunction])
                    {
                        GetOrDeclareSpecialVariable(owner, kind).IsCaptured = true;
                    }
                }
            }
        }

        // The special bindings used by nested functions are declared first: the function then reads them from its environment too
        foreach (var (_, scope, kind) in _specials)
        {
            var owner = GetThisFunction(scope.Function);
            if (owner != scope.Function && owner.Kind is not FunctionInfoKind.Script and not FunctionInfoKind.Module and not FunctionInfoKind.Eval)
            {
                GetOrDeclareSpecialVariable(owner, kind).IsCaptured = true;
            }
        }

        foreach (var (node, scope, kind) in _specials)
        {
            SpecialReferences[(node, kind)] = ResolveSpecial(scope, kind);
        }

        foreach (var pending in _pending)
        {
            References[pending.Node] = Resolve(pending);
        }

        // A mapped arguments object aliases the parameters: they are stored in the environment
        foreach (var scope in _allScopes)
        {
            var function = scope.Function;
            if (scope == function.Scope && function.ArgumentsVariable is { } arguments && (arguments.IsReferenced || function.HasDirectEval) && function.UsesMappedArguments)
            {
                foreach (var variable in scope.OrderedVariables)
                {
                    if (variable.Kind == VariableKind.Parameter)
                    {
                        variable.IsCaptured = true;
                    }
                }
            }
        }

        // Everything visible from a direct eval can be referenced by name: it is stored in the environments
        foreach (var scope in _allScopes)
        {
            if (scope.ContainsDirectEval)
            {
                foreach (var variable in scope.OrderedVariables)
                {
                    variable.IsCaptured = true;
                }
            }
        }

        Allocate();
    }

    /// <summary>The function whose this binding is used by the code of a function (an arrow function uses the one of its enclosing function).</summary>
    private static FunctionInfo GetThisFunction(FunctionInfo function)
    {
        while (!function.HasOwnThis && function.Parent is not null)
        {
            function = function.Parent;
        }

        return function;
    }

    private static Variable GetOrDeclareSpecialVariable(FunctionInfo function, VariableKind kind)
    {
        var existing = kind switch
        {
            VariableKind.This => function.ThisVariable,
            VariableKind.NewTarget => function.NewTargetVariable,
            VariableKind.HomeObject => function.HomeObjectVariable,
            _ => function.ActiveFunctionVariable,
        };

        if (existing is not null)
            return existing;

        var variable = function.Scope.Declare(GetSpecialName(kind), kind);
        switch (kind)
        {
            case VariableKind.This:
                function.ThisVariable = variable;
                break;
            case VariableKind.NewTarget:
                function.NewTargetVariable = variable;
                break;
            case VariableKind.HomeObject:
                function.HomeObjectVariable = variable;
                break;
            default:
                function.ActiveFunctionVariable = variable;
                break;
        }

        return variable;
    }

    private static SpecialReference ResolveSpecial(Scope scope, VariableKind kind)
    {
        var name = GetSpecialName(kind);
        var function = scope.Function;
        var owner = GetThisFunction(function);
        switch (owner.Kind)
        {
            case FunctionInfoKind.Script:
                return new SpecialReference(kind == VariableKind.This ? SpecialReferenceKind.GlobalThis : SpecialReferenceKind.Undefined, variable: null, name);
            case FunctionInfoKind.Module:
                return new SpecialReference(SpecialReferenceKind.Undefined, variable: null, name);
            case FunctionInfoKind.Eval:
                return new SpecialReference(SpecialReferenceKind.Dynamic, variable: null, name);
        }

        // The function itself uses its frame, unless the binding is in its environment (arrow functions, eval)
        if (owner == function)
        {
            var own = kind switch
            {
                VariableKind.This => owner.ThisVariable,
                VariableKind.NewTarget => owner.NewTargetVariable,
                VariableKind.HomeObject => owner.HomeObjectVariable,
                _ => owner.ActiveFunctionVariable,
            };

            return own is null ? new SpecialReference(SpecialReferenceKind.Frame, variable: null, name) : new SpecialReference(SpecialReferenceKind.Variable, own, name);
        }

        var variable = GetOrDeclareSpecialVariable(owner, kind);
        variable.IsCaptured = true;
        return new SpecialReference(SpecialReferenceKind.Variable, variable, name);
    }

    private Reference Resolve(PendingReference pending)
    {
        var dynamic = false;
        for (var scope = pending.Scope; scope is not null; scope = scope.Parent)
        {
            if (scope.Kind == ScopeKind.With)
            {
                dynamic = true;
                continue;
            }

            if (scope.Find(pending.Name) is { } variable)
            {
                variable.IsReferenced = true;
                if (variable.Location == VariableLocation.Dynamic)
                    return new Reference(pending.Name, variable: null, isDynamic: true, needsTdzCheck: false);

                if (scope.Function != pending.Scope.Function || dynamic)
                {
                    variable.IsCaptured = true;
                }

                if (dynamic)
                    return new Reference(pending.Name, variable, isDynamic: true, needsTdzCheck: false);

                var needsTdz = variable.HasTdz && pending.Kind != ReferenceKind.Declaration && !IsInitializedAt(variable, pending);
                return new Reference(pending.Name, variable, isDynamic: false, needsTdz);
            }

            // Sloppy eval code may add a var of the same name to this variable environment at runtime
            if ((scope.IsVarScope && scope.HasSloppyEvalVars) || scope.HasParameterEvalVars)
            {
                dynamic = true;
            }

            // The code of an eval resolves the other names in the environments of its caller
            if (scope.Kind == ScopeKind.Eval)
            {
                dynamic = true;
            }
        }

        return new Reference(pending.Name, variable: null, isDynamic: dynamic, needsTdzCheck: false);
    }

    /// <summary>Whether a binding is always initialized when the reference is evaluated (the reference follows the declaration in the same function, outside a switch).</summary>
    private static bool IsInitializedAt(Variable variable, PendingReference pending)
    {
        if (variable.Scope.Function != pending.Scope.Function)
            return false;

        if (variable.Scope.Kind == ScopeKind.Switch)
            return false;

        return pending.Node.Start >= variable.DeclarationEnd;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Allocation of the registers and the environment slots
    // ------------------------------------------------------------------------------------------------------------------

    private void Allocate()
    {
        foreach (var scope in _allScopes)
        {
            var isGlobalCode = scope.Kind == ScopeKind.Script;
            var isModule = scope.Kind == ScopeKind.Module;
            var slots = 0;
            foreach (var variable in scope.OrderedVariables)
            {
                if (variable.Location != VariableLocation.Unallocated)
                    continue;

                if (isGlobalCode)
                {
                    variable.Location = variable.Kind is VariableKind.Var or VariableKind.Function ? VariableLocation.GlobalObject : VariableLocation.GlobalLexical;
                    continue;
                }

                if (variable.IsCaptured || isModule || variable.Kind == VariableKind.PrivateName)
                {
                    variable.Location = VariableLocation.Context;
                    variable.Index = slots++;
                }
                else
                {
                    variable.Location = VariableLocation.Register;
                    variable.Index = scope.Function.AllocateRegister();
                }
            }

            scope.ContextSlotCount = slots;
            if (slots > 0 || scope.Kind == ScopeKind.With || isModule || (scope.IsVarScope && scope.HasSloppyEvalVars && !isGlobalCode) || scope.HasParameterEvalVars)
            {
                scope.NeedsContext = true;
            }

            // A scope that eval code can see has an environment, so that the eval code finds its bindings by name
            if (scope.ContainsDirectEval && !isGlobalCode && scope.OrderedVariables.Count > 0)
            {
                scope.NeedsContext = true;
            }

            if (scope.NeedsContext && scope.Kind != ScopeKind.With)
            {
                scope.Info = CreateScopeInfo(scope);
            }
        }
    }

    private static ScopeInfo CreateScopeInfo(Scope scope)
    {
        var names = new string?[scope.ContextSlotCount];
        var flags = new ScopeSlotFlags[scope.ContextSlotCount];
        foreach (var variable in scope.OrderedVariables)
        {
            if (variable.Location != VariableLocation.Context)
                continue;

            names[variable.Index] = variable.Name;
            var slotFlags = ScopeSlotFlags.None;
            if (variable.HasTdz || variable.Kind is VariableKind.This or VariableKind.BlockFunction)
            {
                slotFlags |= ScopeSlotFlags.Uninitialized;
            }

            if (variable.Kind is VariableKind.Const or VariableKind.ClassName or VariableKind.Import)
            {
                slotFlags |= ScopeSlotFlags.Immutable;
            }

            if (variable.Kind == VariableKind.FunctionName)
            {
                slotFlags |= ScopeSlotFlags.FunctionName;
            }

            if (variable.Kind == VariableKind.Import)
            {
                slotFlags |= ScopeSlotFlags.Import;
            }

            flags[variable.Index] = slotFlags;
        }

        return new ScopeInfo(scope.Kind, names, flags, scope.IsVarScope || scope.HasParameterEvalVars, scope.HasParameterEvalVars);
    }
}
