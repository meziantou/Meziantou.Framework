namespace Meziantou.Framework.JavaScriptEngine.Parsing;

internal sealed partial class Parser
{
    private readonly List<Scope> _scopes = [];
    private int _scopeCount;

    [Flags]
    private enum ScopeFlags
    {
        None = 0,
        Top = 1 << 0,
        Function = 1 << 1,
        Async = 1 << 2,
        Generator = 1 << 3,
        Arrow = 1 << 4,
        SimpleCatch = 1 << 5,

        /// <summary><c>super.x</c> is allowed (methods, accessors, field initializers, static blocks).</summary>
        Super = 1 << 6,

        /// <summary><c>super()</c> is allowed (derived constructors).</summary>
        DirectSuper = 1 << 7,
        ClassStaticBlock = 1 << 8,
        ClassFieldInit = 1 << 9,

        /// <summary>The scopes that hold the var declarations.</summary>
        Var = Top | Function | ClassStaticBlock | ClassFieldInit,
    }

    private enum BindingKind
    {
        /// <summary>An assignment target, not a binding.</summary>
        None,
        Var,
        Lexical,

        /// <summary>A function declaration in a block (sloppy mode): duplicates are allowed (Annex B).</summary>
        Function,

        /// <summary>The identifier of a catch clause: a var with the same name is allowed in the block (Annex B).</summary>
        SimpleCatch,

        /// <summary>Only checks the name (strict mode restrictions), without declaring it.</summary>
        Outside,
    }

    private sealed class Scope
    {
        public ScopeFlags Flags;
        public HashSet<string>? Var;
        public HashSet<string>? Lexical;
        public HashSet<string>? Functions;
        public string? CatchName;

        public static bool Contains(HashSet<string>? set, string name) => set is not null && set.Contains(name);

        public void Reset(ScopeFlags flags)
        {
            Flags = flags;
            Var?.Clear();
            Lexical?.Clear();
            Functions?.Clear();
            CatchName = null;
        }
    }

    private Scope CurrentScope => _scopes[_scopeCount - 1];

    private Scope TopScope => _scopes[0];

    private void EnterScope(ScopeFlags flags)
    {
        if (_scopeCount < _scopes.Count)
        {
            _scopes[_scopeCount].Reset(flags);
        }
        else
        {
            _scopes.Add(new Scope { Flags = flags });
        }

        _scopeCount++;
    }

    private void ExitScope()
    {
        _scopeCount--;
    }

    private Scope CurrentVarScope
    {
        get
        {
            for (var i = _scopeCount - 1; ; i--)
            {
                var scope = _scopes[i];
                if ((scope.Flags & ScopeFlags.Var) != 0)
                    return scope;
            }
        }
    }

    /// <summary>The scope that provides 'this' (the nearest non-arrow function, field initializer, static block or the top level).</summary>
    private Scope CurrentThisScope
    {
        get
        {
            for (var i = _scopeCount - 1; ; i--)
            {
                var scope = _scopes[i];
                if ((scope.Flags & ScopeFlags.Var) != 0 && (scope.Flags & ScopeFlags.Arrow) == 0)
                    return scope;
            }
        }
    }

    private bool InFunction => (CurrentVarScope.Flags & ScopeFlags.Function) != 0;

    private bool InGenerator => (CurrentVarScope.Flags & ScopeFlags.Generator) != 0;

    private bool InAsync => (CurrentVarScope.Flags & ScopeFlags.Async) != 0;

    private bool InClassStaticBlock => (CurrentVarScope.Flags & ScopeFlags.ClassStaticBlock) != 0;

    /// <summary>Gets whether <c>await</c> is an AwaitExpression here.</summary>
    private bool CanAwait
    {
        get
        {
            for (var i = _scopeCount - 1; i >= 0; i--)
            {
                var scope = _scopes[i];
                if ((scope.Flags & (ScopeFlags.ClassFieldInit | ScopeFlags.ClassStaticBlock)) != 0)
                    return false;

                if ((scope.Flags & ScopeFlags.Function) != 0)
                    return (scope.Flags & ScopeFlags.Async) != 0;
            }

            return _isModule;
        }
    }

    private bool AllowSuperProperty
    {
        get
        {
            var scope = CurrentThisScope;
            if ((scope.Flags & ScopeFlags.Top) != 0)
                return _options.AllowSuperProperty;

            return (scope.Flags & (ScopeFlags.Super | ScopeFlags.ClassFieldInit)) != 0;
        }
    }

    private bool AllowSuperCall
    {
        get
        {
            var scope = CurrentThisScope;
            if ((scope.Flags & ScopeFlags.Top) != 0)
                return _options.AllowSuperCall;

            return (scope.Flags & ScopeFlags.DirectSuper) != 0;
        }
    }

    private bool AllowNewTarget
    {
        get
        {
            var scope = CurrentThisScope;
            if ((scope.Flags & ScopeFlags.Top) != 0)
                return _options.AllowNewTarget;

            return true;
        }
    }

    /// <summary>Gets whether 'arguments' is forbidden (class field initializers and static blocks, ContainsArguments).</summary>
    private bool ArgumentsForbidden
    {
        get
        {
            var scope = CurrentThisScope;
            if ((scope.Flags & ScopeFlags.Top) != 0)
                return _options.InClassFieldInitializer;

            return (scope.Flags & (ScopeFlags.ClassFieldInit | ScopeFlags.ClassStaticBlock)) != 0;
        }
    }

    private bool TreatFunctionsAsVarInScope(Scope scope) => (scope.Flags & (ScopeFlags.Function | ScopeFlags.ClassStaticBlock)) != 0 || (!_isModule && (scope.Flags & ScopeFlags.Top) != 0);

    private bool TreatFunctionsAsVar => TreatFunctionsAsVarInScope(CurrentScope);

    private void DeclareName(string name, BindingKind kind, int position)
    {
        var redeclared = false;
        switch (kind)
        {
            case BindingKind.Lexical:
            {
                var scope = CurrentScope;
                redeclared = Scope.Contains(scope.Lexical, name) || Scope.Contains(scope.Functions, name) || Scope.Contains(scope.Var, name);
                (scope.Lexical ??= new(StringComparer.Ordinal)).Add(name);
                break;
            }

            case BindingKind.SimpleCatch:
            {
                var scope = CurrentScope;
                (scope.Lexical ??= new(StringComparer.Ordinal)).Add(name);
                scope.CatchName = name;
                break;
            }

            case BindingKind.Function:
            {
                var scope = CurrentScope;
                redeclared = TreatFunctionsAsVar
                    ? Scope.Contains(scope.Lexical, name)
                    : Scope.Contains(scope.Lexical, name) || Scope.Contains(scope.Var, name);
                (scope.Functions ??= new(StringComparer.Ordinal)).Add(name);
                break;
            }

            case BindingKind.Var:
                for (var i = _scopeCount - 1; i >= 0; i--)
                {
                    var scope = _scopes[i];
                    if ((Scope.Contains(scope.Lexical, name) && !((scope.Flags & ScopeFlags.SimpleCatch) != 0 && scope.CatchName == name)) ||
                        (!TreatFunctionsAsVarInScope(scope) && Scope.Contains(scope.Functions, name)))
                    {
                        redeclared = true;
                        break;
                    }

                    (scope.Var ??= new(StringComparer.Ordinal)).Add(name);
                    if ((scope.Flags & ScopeFlags.Var) != 0)
                        break;
                }

                break;
        }

        if (redeclared)
            Raise(position, "Identifier '" + name + "' has already been declared");
    }
}
