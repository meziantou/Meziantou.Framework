using Meziantou.Framework.JavaScriptEngine.Ast;

namespace Meziantou.Framework.JavaScriptEngine.Compiler;

/// <summary>
/// The lazy compilation of the nested functions. A nested function is not analyzed with its enclosing code: the code only
/// needs to know what the function can reference, to store these bindings in the environments (the names of its
/// identifiers, and the this, new.target and super of its enclosing function for an arrow function). The function is
/// compiled on its first call, in the scopes that enclose it (<see cref="ScopeSnapshot"/>). A function that contains a
/// direct eval is analyzed with its enclosing code: the eval code can see everything.
/// </summary>
internal sealed partial class ScopeAnalyzer
{
    private readonly List<(string Name, Scope Scope)> _lazyReferences = [];
    private readonly List<(Scope Scope, VariableKind Kind)> _lazySpecials = [];
    private readonly Dictionary<Scope, ScopeSnapshot> _scopeSnapshots = [];
    private readonly Dictionary<FunctionInfo, FunctionSnapshot> _functionSnapshots = [];
    private LazyFunctionScanner? _scanner;

    /// <summary>Gets or sets the function compiled with the program (the function of a dynamic function).</summary>
    private FunctionNode? _eagerFunction;

    /// <summary>Visits a nested function: it is compiled on its first call, unless it contains a direct eval.</summary>
    private void VisitNestedFunction(FunctionNode function, Scope parentScope, FunctionInfoKind kind)
    {
        if (ReferenceEquals(function, _eagerFunction))
        {
            VisitFunction(function, parentScope, kind);
            return;
        }

        var scanner = _scanner ??= new LazyFunctionScanner();
        scanner.Scan(function, isArrow: kind == FunctionInfoKind.Arrow);
        if (scanner.HasDirectEval)
        {
            VisitFunction(function, parentScope, kind);
            return;
        }

        var outer = parentScope;
        if (kind is FunctionInfoKind.Normal && function.Id is not null && IsFunctionExpression(function))
        {
            outer = CreateFunctionNameScope(function, parentScope);
        }

        var info = new FunctionInfo(parentScope.Function, function, kind, function.IsStrict) { LazyScope = outer };
        parentScope.Function.Children.Add(info);
        Functions[function] = info;
        foreach (var name in scanner.Names)
        {
            _lazyReferences.Add((name, outer));
        }

        foreach (var special in (ReadOnlySpan<VariableKind>)[VariableKind.This, VariableKind.NewTarget, VariableKind.HomeObject, VariableKind.ActiveFunction])
        {
            if (scanner.UsesSpecial(special))
            {
                _lazySpecials.Add((outer, special));
            }
        }
    }

    private Scope CreateFunctionNameScope(FunctionNode function, Scope parentScope)
    {
        var nameScope = CreateScope(ScopeKind.FunctionName, parentScope, parentScope.Function, node: null);
        nameScope.Declare(function.Id!.Name, VariableKind.FunctionName);
        _functionNameScopes[function] = nameScope;
        return nameScope;
    }

    /// <summary>The bindings of the enclosing functions that the lazily compiled arrow functions use (this, new.target, super): they are stored in the environments.</summary>
    private void ResolveLazySpecials()
    {
        foreach (var (scope, kind) in _lazySpecials)
        {
            var owner = GetThisFunction(scope.Function);
            if (owner.Kind is not FunctionInfoKind.Script and not FunctionInfoKind.Module and not FunctionInfoKind.Eval)
            {
                GetOrDeclareSpecialVariable(owner, kind).IsCaptured = true;
            }
        }
    }

    /// <summary>The bindings that a lazily compiled function may reference: they are referenced by another function (stored in the environments).</summary>
    private void ResolveLazyReferences()
    {
        foreach (var (name, scope) in _lazyReferences)
        {
            for (var current = scope; current is not null; current = current.Parent)
            {
                if (current.Kind != ScopeKind.With && current.Find(name) is { } variable)
                {
                    CheckNotRegisterOfEnclosingFunction(variable);
                    variable.IsReferenced = true;
                    if (variable.Location != VariableLocation.Dynamic)
                    {
                        variable.IsCaptured = true;
                    }

                    break;
                }
            }
        }
    }

    /// <summary>
    /// A binding of the code that encloses a lazily compiled function can only be referenced by the function when it is in
    /// an environment: the enclosing code stored every binding whose name the function contains.
    /// </summary>
    private static void CheckNotRegisterOfEnclosingFunction(Variable variable)
    {
        if (variable.Scope.Snapshot is not null && variable.Location == VariableLocation.Register)
            throw new InvalidOperationException("The lazily compiled function references the binding '" + variable.Name + "', which is not in an environment of its enclosing code");
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Snapshots
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>Gets the immutable copy of a scope (and of the scopes that enclose it) for a lazily compiled function.</summary>
    public ScopeSnapshot GetScopeSnapshot(Scope scope)
    {
        if (TryGetExistingSnapshot(scope) is { } existing)
            return existing;

        // From the outermost scope without a snapshot (the chain can be long: no recursion)
        var chain = new List<Scope>();
        for (var current = scope; current is not null && TryGetExistingSnapshot(current) is null; current = current.Parent)
        {
            chain.Add(current);
        }

        ScopeSnapshot? snapshot = null;
        for (var i = chain.Count - 1; i >= 0; i--)
        {
            var current = chain[i];
            var parent = current.Parent is null ? null : TryGetExistingSnapshot(current.Parent);
            var variables = new VariableSnapshot[current.OrderedVariables.Count];
            for (var j = 0; j < variables.Length; j++)
            {
                var variable = current.OrderedVariables[j];
                variables[j] = new VariableSnapshot(variable.Name, variable.Kind, variable.Location, variable.Index, variable.IsParameterWithTdz);
            }

            snapshot = new ScopeSnapshot(current.Kind, parent, GetFunctionSnapshot(current.Function), current.NeedsContext, current.IsVarScope && current.HasSloppyEvalVars, current.HasParameterEvalVars, variables);
            _scopeSnapshots[current] = snapshot;
        }

        return snapshot!;
    }

    private ScopeSnapshot? TryGetExistingSnapshot(Scope scope) => scope.Snapshot ?? _scopeSnapshots.GetValueOrDefault(scope);

    private FunctionSnapshot GetFunctionSnapshot(FunctionInfo function)
    {
        if (function.Snapshot is { } existing)
            return existing;

        if (_functionSnapshots.TryGetValue(function, out existing))
            return existing;

        var parent = function.Parent is null ? null : GetFunctionSnapshot(function.Parent);
        var snapshot = new FunctionSnapshot(function.Kind, function.IsStrict, parent);
        _functionSnapshots[function] = snapshot;
        return snapshot;
    }

    /// <summary>Analyzes a lazily compiled function in the scopes that enclose it.</summary>
    public static ScopeAnalyzer AnalyzeLazyFunction(FunctionNode function, FunctionInfoKind kind, ScopeSnapshot scope)
    {
        var analyzer = new ScopeAnalyzer(ProgramKind.Script);
        analyzer._pending.EnsureCapacity(Math.Max(16, (function.End - function.Start) / 10));
        var outer = Materialize(scope);
        analyzer.Root = analyzer.VisitFunction(function, outer, kind);
        analyzer.ResolveAll();
        return analyzer;
    }

    /// <summary>Creates the scopes of a snapshot, whose bindings are allocated (they are new objects: the snapshot is shared by the threads).</summary>
    private static Scope Materialize(ScopeSnapshot snapshot)
    {
        var chain = new List<ScopeSnapshot>();
        for (var current = snapshot; current is not null; current = current.Parent)
        {
            chain.Add(current);
        }

        var functions = new Dictionary<FunctionSnapshot, FunctionInfo>();
        Scope? scope = null;
        for (var i = chain.Count - 1; i >= 0; i--)
        {
            var current = chain[i];
            var function = MaterializeFunction(current.Function, functions);
            scope = new Scope(current.Kind, scope, function, node: null)
            {
                NeedsContext = current.NeedsContext,
                HasSloppyEvalVars = current.HasSloppyEvalVars,
                HasParameterEvalVars = current.HasParameterEvalVars,
                Snapshot = current,
            };

            switch (current.Kind)
            {
                case ScopeKind.Function:
                    function.Scope = scope;
                    break;
                case ScopeKind.FunctionBody:
                    function.BodyVarScope = scope;
                    break;
            }

            foreach (var variableSnapshot in current.Variables)
            {
                var variable = scope.Declare(variableSnapshot.Name, variableSnapshot.Kind);
                variable.Location = variableSnapshot.Location;
                variable.Index = variableSnapshot.Index;
                variable.IsParameterWithTdz = variableSnapshot.IsParameterWithTdz;
                variable.IsCaptured = variableSnapshot.Location == VariableLocation.Context;
                switch (variableSnapshot.Kind)
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
                    case VariableKind.ActiveFunction:
                        function.ActiveFunctionVariable = variable;
                        break;
                }
            }
        }

        return scope!;
    }

    private static FunctionInfo MaterializeFunction(FunctionSnapshot snapshot, Dictionary<FunctionSnapshot, FunctionInfo> functions)
    {
        if (functions.TryGetValue(snapshot, out var function))
            return function;

        var parent = snapshot.Parent is null ? null : MaterializeFunction(snapshot.Parent, functions);
        function = new FunctionInfo(parent, node: null, snapshot.Kind, snapshot.IsStrict) { Snapshot = snapshot };
        functions[snapshot] = function;
        return function;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Scanner
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Collects what a function can reference outside of itself: the names of its free identifiers (a reference whose name
    /// is not declared by an enclosing scope of the function), the special bindings of its enclosing function that an arrow
    /// function uses, and whether it contains a direct eval. The scopes are those of the analysis, simplified: a name that
    /// is not certainly declared inside the function (e.g. the var of a function declared in a block, Annex B) is free.
    /// </summary>
    private sealed class LazyFunctionScanner
    {
        // The names declared by the scopes of the function that enclose the current node (innermost last), reused
        private readonly List<HashSet<string>> _scopes = [];
        private readonly List<Node> _leftNestedExpressions = [];
        private int _scopeCount;
        private int _specials;

        public HashSet<string> Names { get; } = new(StringComparer.Ordinal);

        public bool HasDirectEval { get; private set; }

        public bool UsesSpecial(VariableKind kind) => (_specials & GetSpecialFlag(kind)) != 0;

        private static int GetSpecialFlag(VariableKind kind) => 1 << (kind - VariableKind.This);

        public void Scan(FunctionNode function, bool isArrow)
        {
            Names.Clear();
            HasDirectEval = false;
            _specials = 0;
            _scopeCount = 0;

            // The name of the function is in the scope of its name, outside of the function (the enclosing code declares it)
            ScanFunction(function, inThisScope: isArrow, ownName: null);
        }

        private HashSet<string> PushScope()
        {
            if (_scopeCount == _scopes.Count)
            {
                _scopes.Add(new HashSet<string>(StringComparer.Ordinal));
            }

            var scope = _scopes[_scopeCount++];
            scope.Clear();
            return scope;
        }

        private void PopScope() => _scopeCount--;

        private void Reference(string name)
        {
            for (var i = _scopeCount - 1; i >= 0; i--)
            {
                if (_scopes[i].Contains(name))
                    return;
            }

            Names.Add(name);
        }

        // inThisScope: the code uses the this, new.target and super of the enclosing function (an arrow function and the
        // arrow functions it contains, the computed keys and the heritage of the classes they contain)
        private void ScanFunction(FunctionNode function, bool inThisScope, string? ownName)
        {
            var scope = PushScope();
            if (ownName is not null)
            {
                scope.Add(ownName);
            }

            // A function that is not an arrow function has its own arguments (a binding, a parameter or a declaration)
            if (function.Kind != FunctionKind.Arrow)
            {
                scope.Add("arguments");
            }

            foreach (var parameter in function.Params)
            {
                DeclareBoundNames(parameter, scope);
            }

            // The declarations of the body are not visible from the expressions of the parameters
            var body = function.Body as BlockStatement;
            var hasParameterExpressions = !function.HasSimpleParameterList;
            if (body is not null && !hasParameterExpressions)
            {
                DeclareFunctionBody(body.Body, scope);
            }

            ScanList(function.Params, inThisScope);
            if (body is null)
            {
                Scan(function.Body, inThisScope);
            }
            else if (hasParameterExpressions)
            {
                DeclareFunctionBody(body.Body, PushScope());
                ScanList(body.Body, inThisScope);
                PopScope();
            }
            else
            {
                ScanList(body.Body, inThisScope);
            }

            PopScope();
        }

        private void UseSpecial(VariableKind kind, bool inThisScope)
        {
            if (inThisScope)
            {
                _specials |= GetSpecialFlag(kind);
            }
        }

        private void ScanList<T>(List<T> nodes, bool inThisScope)
            where T : Node?
        {
            foreach (var node in nodes)
            {
                Scan(node, inThisScope);
            }
        }

        /// <summary>Scans the statements of a block (or a switch): its lexical declarations are in its scope.</summary>
        private void ScanBlock(List<Node> statements, bool inThisScope)
        {
            var scope = PushScope();
            foreach (var statement in statements)
            {
                DeclareLexical(statement, scope);
            }

            ScanList(statements, inThisScope);
            PopScope();
        }

        private void ScanClass(ClassNode node, bool inThisScope)
        {
            // The decorators of the class are evaluated outside of its scope
            ScanDecorators(node.Decorators, inThisScope);

            // The inner binding of the name and the private names are in the scope of the class
            var scope = PushScope();
            if (node.Id is not null)
            {
                scope.Add(node.Id.Name);
            }

            foreach (var element in node.Body)
            {
                var key = element switch
                {
                    MethodDefinition method => method.Key,
                    PropertyDefinition property => property.Key,
                    _ => null,
                };

                if (key is PrivateIdentifier privateName)
                {
                    scope.Add("#" + privateName.Name);
                }
            }

            Scan(node.SuperClass, inThisScope);
            foreach (var element in node.Body)
            {
                switch (element)
                {
                    case MethodDefinition method:
                        ScanDecorators(method.Decorators, inThisScope);
                        ScanKey(method.Key, method.Computed, inThisScope);
                        ScanFunction(method.Value, inThisScope: false, ownName: null);
                        break;
                    case PropertyDefinition property:
                        ScanDecorators(property.Decorators, inThisScope);
                        ScanKey(property.Key, property.Computed, inThisScope);
                        Scan(property.Value, inThisScope: false);
                        break;
                    case StaticBlock block:
                        // A static block has its own variable environment
                        DeclareFunctionBody(block.Body, PushScope());
                        ScanList(block.Body, inThisScope: false);
                        PopScope();
                        break;
                    default:
                        throw new InvalidOperationException("Unexpected class element: " + element.Type);
                }
            }

            PopScope();
        }

        private void ScanDecorators(List<Node>? decorators, bool inThisScope)
        {
            if (decorators is not null)
            {
                ScanList(decorators, inThisScope);
            }
        }

        private void ScanKey(Node key, bool computed, bool inThisScope)
        {
            if (computed)
            {
                Scan(key, inThisScope);
            }
            else if (key is PrivateIdentifier privateName)
            {
                Reference("#" + privateName.Name);
            }
        }

        /// <summary>Declares the var-scoped and the top-level lexical declarations of the body of a function (or of a static block).</summary>
        private static void DeclareFunctionBody(List<Node> statements, HashSet<string> scope)
        {
            foreach (var statement in statements)
            {
                DeclareVarScoped(statement, scope, isTopLevel: true);
                DeclareLexical(statement, scope);
            }
        }

        /// <summary>Declares the VarScopedDeclarations of a statement (see <see cref="ScopeAnalyzer.DeclareVarScoped"/>).</summary>
        private static void DeclareVarScoped(Node? statement, HashSet<string> scope, bool isTopLevel)
        {
            CompilerStack.EnsureSufficientStack();
            switch (statement)
            {
                case VariableDeclaration { Kind: VariableDeclarationKind.Var } declaration:
                    foreach (var declarator in declaration.Declarations)
                    {
                        DeclareBoundNames(declarator.Id, scope);
                    }

                    break;
                case FunctionDeclaration function when isTopLevel:
                    scope.Add(function.Function.Id!.Name);
                    break;
                case BlockStatement block:
                    foreach (var child in block.Body)
                    {
                        DeclareVarScoped(child, scope, isTopLevel: false);
                    }

                    break;
                case IfStatement ifStatement:
                    DeclareVarScoped(ifStatement.Consequent, scope, isTopLevel: false);
                    DeclareVarScoped(ifStatement.Alternate, scope, isTopLevel: false);
                    break;
                case WhileStatement whileStatement:
                    DeclareVarScoped(whileStatement.Body, scope, isTopLevel: false);
                    break;
                case DoWhileStatement doWhile:
                    DeclareVarScoped(doWhile.Body, scope, isTopLevel: false);
                    break;
                case ForStatement forStatement:
                    DeclareVarScoped(forStatement.Init, scope, isTopLevel: false);
                    DeclareVarScoped(forStatement.Body, scope, isTopLevel: false);
                    break;
                case ForInStatement forIn:
                    DeclareVarScoped(forIn.Left, scope, isTopLevel: false);
                    DeclareVarScoped(forIn.Body, scope, isTopLevel: false);
                    break;
                case ForOfStatement forOf:
                    DeclareVarScoped(forOf.Left, scope, isTopLevel: false);
                    DeclareVarScoped(forOf.Body, scope, isTopLevel: false);
                    break;
                case LabeledStatement labeled:
                    DeclareVarScoped(labeled.Body, scope, isTopLevel && labeled.Body is FunctionDeclaration or LabeledStatement);
                    break;
                case SwitchStatement switchStatement:
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        foreach (var child in switchCase.Consequent)
                        {
                            DeclareVarScoped(child, scope, isTopLevel: false);
                        }
                    }

                    break;
                case TryStatement tryStatement:
                    DeclareVarScoped(tryStatement.Block, scope, isTopLevel: false);
                    DeclareVarScoped(tryStatement.Handler?.Body, scope, isTopLevel: false);
                    DeclareVarScoped(tryStatement.Finalizer, scope, isTopLevel: false);
                    break;
                case WithStatement withStatement:
                    DeclareVarScoped(withStatement.Body, scope, isTopLevel: false);
                    break;
            }
        }

        /// <summary>Declares the LexicallyScopedDeclarations of a statement of a statement list (the function declarations of a block are block-scoped).</summary>
        private static void DeclareLexical(Node? statement, HashSet<string> scope)
        {
            switch (statement)
            {
                case VariableDeclaration { Kind: not VariableDeclarationKind.Var } declaration:
                    foreach (var declarator in declaration.Declarations)
                    {
                        DeclareBoundNames(declarator.Id, scope);
                    }

                    break;
                case ClassDeclaration classDeclaration:
                    scope.Add(classDeclaration.Class.Id!.Name);
                    break;
                case FunctionDeclaration function:
                    scope.Add(function.Function.Id!.Name);
                    break;
                case LabeledStatement labeled:
                    DeclareLexical(labeled.Body, scope);
                    break;
            }
        }

        /// <summary>Declares the BoundNames of a binding target (an identifier or a pattern).</summary>
        private static void DeclareBoundNames(Node? target, HashSet<string> scope)
        {
            CompilerStack.EnsureSufficientStack();
            switch (target)
            {
                case Identifier identifier:
                    scope.Add(identifier.Name);
                    break;
                case ObjectPattern objectPattern:
                    foreach (var property in objectPattern.Properties)
                    {
                        DeclareBoundNames(property is Property p ? p.Value : ((RestElement)property).Argument, scope);
                    }

                    break;
                case ArrayPattern arrayPattern:
                    foreach (var element in arrayPattern.Elements)
                    {
                        DeclareBoundNames(element, scope);
                    }

                    break;
                case AssignmentPattern assignment:
                    DeclareBoundNames(assignment.Left, scope);
                    break;
                case RestElement rest:
                    DeclareBoundNames(rest.Argument, scope);
                    break;
            }
        }

        // Long chains (a.b().c()..., a + b + c...) nest on their left operand: see ScopeAnalyzer.VisitLeftNestedExpression
        private void ScanLeftNestedExpression(Node node, bool inThisScope)
        {
            var start = _leftNestedExpressions.Count;
            for (Node? current = node; current is not null; current = GetLeftNestedOperand(current))
            {
                _leftNestedExpressions.Add(current);
            }

            switch (_leftNestedExpressions[^1])
            {
                case BinaryExpression binary:
                    Scan(binary.Left, inThisScope);
                    break;
                case LogicalExpression logical:
                    Scan(logical.Left, inThisScope);
                    break;
                case CallExpression call:
                    if (call.Callee is Identifier { Name: "eval" } && !call.Optional)
                    {
                        HasDirectEval = true;
                    }

                    if (call.Callee is Super)
                    {
                        // super(): the this binding, new.target and the active function of the derived constructor
                        UseSpecial(VariableKind.ActiveFunction, inThisScope);
                        UseSpecial(VariableKind.This, inThisScope);
                        UseSpecial(VariableKind.NewTarget, inThisScope);
                    }
                    else
                    {
                        Scan(call.Callee, inThisScope);
                    }

                    break;
                case MemberExpression member:
                    if (member.Object is Super)
                    {
                        UseSpecial(VariableKind.HomeObject, inThisScope);
                        UseSpecial(VariableKind.This, inThisScope);
                    }
                    else
                    {
                        Scan(member.Object, inThisScope);
                    }

                    break;
            }

            for (var i = _leftNestedExpressions.Count - 1; i >= start; i--)
            {
                switch (_leftNestedExpressions[i])
                {
                    case BinaryExpression binary:
                        Scan(binary.Right, inThisScope);
                        break;
                    case LogicalExpression logical:
                        Scan(logical.Right, inThisScope);
                        break;
                    case CallExpression call:
                        ScanList(call.Arguments, inThisScope);
                        break;
                    case MemberExpression member:
                        ScanKey(member.Property, member.Computed, inThisScope);
                        break;
                }
            }

            _leftNestedExpressions.RemoveRange(start, _leftNestedExpressions.Count - start);

            static Node? GetLeftNestedOperand(Node node)
            {
                var left = node switch
                {
                    BinaryExpression binary => binary.Left,
                    LogicalExpression logical => logical.Left,
                    CallExpression call => call.Callee,
                    MemberExpression member => member.Object,
                    _ => null,
                };

                return left is BinaryExpression or LogicalExpression or CallExpression or MemberExpression ? left : null;
            }
        }

        private void Scan(Node? node, bool inThisScope)
        {
            CompilerStack.EnsureSufficientStack();
            switch (node)
            {
                case null:
                case EmptyStatement:
                case DebuggerStatement:
                case BreakStatement:
                case ContinueStatement:
                case NullLiteral:
                case BooleanLiteral:
                case NumericLiteral:
                case BigIntLiteral:
                case StringLiteral:
                case RegExpLiteral:
                case TemplateElement:
                    return;
                case Identifier identifier:
                    Reference(identifier.Name);
                    return;
                case PrivateIdentifier privateName:
                    Reference("#" + privateName.Name);
                    return;
                case ThisExpression:
                    UseSpecial(VariableKind.This, inThisScope);
                    return;
                case Super:
                    UseSpecial(VariableKind.HomeObject, inThisScope);
                    return;
                case MetaProperty meta:
                    if (meta.Kind == MetaPropertyKind.NewTarget)
                    {
                        UseSpecial(VariableKind.NewTarget, inThisScope);
                    }

                    return;
                case ExpressionStatement statement:
                    Scan(statement.Expression, inThisScope);
                    return;
                case BlockStatement block:
                    ScanBlock(block.Body, inThisScope);
                    return;
                case WithStatement withStatement:
                    Scan(withStatement.Object, inThisScope);
                    Scan(withStatement.Body, inThisScope);
                    return;
                case ReturnStatement returnStatement:
                    Scan(returnStatement.Argument, inThisScope);
                    return;
                case LabeledStatement labeled:
                    Scan(labeled.Body, inThisScope);
                    return;
                case IfStatement ifStatement:
                    Scan(ifStatement.Test, inThisScope);
                    Scan(ifStatement.Consequent, inThisScope);
                    Scan(ifStatement.Alternate, inThisScope);
                    return;
                case SwitchStatement switchStatement:
                {
                    Scan(switchStatement.Discriminant, inThisScope);
                    var scope = PushScope();
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        foreach (var statement in switchCase.Consequent)
                        {
                            DeclareLexical(statement, scope);
                        }
                    }

                    ScanList(switchStatement.Cases, inThisScope);
                    PopScope();
                    return;
                }

                case SwitchCase switchCase:
                    Scan(switchCase.Test, inThisScope);
                    ScanList(switchCase.Consequent, inThisScope);
                    return;
                case ThrowStatement throwStatement:
                    Scan(throwStatement.Argument, inThisScope);
                    return;
                case TryStatement tryStatement:
                    Scan(tryStatement.Block, inThisScope);
                    Scan(tryStatement.Handler, inThisScope);
                    Scan(tryStatement.Finalizer, inThisScope);
                    return;
                case CatchClause catchClause:
                    DeclareBoundNames(catchClause.Param, PushScope());
                    Scan(catchClause.Param, inThisScope);
                    Scan(catchClause.Body, inThisScope);
                    PopScope();
                    return;
                case WhileStatement whileStatement:
                    Scan(whileStatement.Test, inThisScope);
                    Scan(whileStatement.Body, inThisScope);
                    return;
                case DoWhileStatement doWhile:
                    Scan(doWhile.Body, inThisScope);
                    Scan(doWhile.Test, inThisScope);
                    return;
                case ForStatement forStatement:
                    // The let and const declarations of the head are in the scope of the loop
                    DeclareLexical(forStatement.Init, PushScope());
                    Scan(forStatement.Init, inThisScope);
                    Scan(forStatement.Test, inThisScope);
                    Scan(forStatement.Update, inThisScope);
                    Scan(forStatement.Body, inThisScope);
                    PopScope();
                    return;
                case ForInStatement forIn:
                    // The expression is evaluated with the let and const declarations of the head in TDZ
                    DeclareLexical(forIn.Left, PushScope());
                    Scan(forIn.Left, inThisScope);
                    Scan(forIn.Right, inThisScope);
                    Scan(forIn.Body, inThisScope);
                    PopScope();
                    return;
                case ForOfStatement forOf:
                    DeclareLexical(forOf.Left, PushScope());
                    Scan(forOf.Left, inThisScope);
                    Scan(forOf.Right, inThisScope);
                    Scan(forOf.Body, inThisScope);
                    PopScope();
                    return;
                case VariableDeclaration declaration:
                    ScanList(declaration.Declarations, inThisScope);
                    return;
                case VariableDeclarator declarator:
                    Scan(declarator.Id, inThisScope);
                    Scan(declarator.Init, inThisScope);
                    return;
                case FunctionDeclaration declaration:
                    Reference(declaration.Function.Id!.Name);
                    ScanFunction(declaration.Function, inThisScope: false, ownName: null);
                    return;
                case ClassDeclaration declaration:
                    Reference(declaration.Class.Id!.Name);
                    ScanClass(declaration.Class, inThisScope);
                    return;
                case TemplateLiteral template:
                    ScanList(template.Expressions, inThisScope);
                    return;
                case TaggedTemplateExpression tagged:
                    Scan(tagged.Tag, inThisScope);
                    Scan(tagged.Quasi, inThisScope);
                    return;
                case ArrayExpression array:
                    ScanList(array.Elements, inThisScope);
                    return;
                case ArrayPattern arrayPattern:
                    ScanList(arrayPattern.Elements, inThisScope);
                    return;
                case ObjectExpression obj:
                    ScanList(obj.Properties, inThisScope);
                    return;
                case ObjectPattern objectPattern:
                    ScanList(objectPattern.Properties, inThisScope);
                    return;
                case Property property:
                    ScanKey(property.Key, property.Computed, inThisScope);
                    if (property.Value is FunctionExpression method && (property.Method || property.Kind != PropertyKind.Init))
                    {
                        ScanFunction(method.Function, inThisScope: false, ownName: null);
                    }
                    else
                    {
                        Scan(property.Value, inThisScope);
                    }

                    return;
                case SpreadElement spread:
                    Scan(spread.Argument, inThisScope);
                    return;
                case RestElement rest:
                    Scan(rest.Argument, inThisScope);
                    return;
                case AssignmentPattern assignment:
                    Scan(assignment.Left, inThisScope);
                    Scan(assignment.Right, inThisScope);
                    return;
                case FunctionExpression function:
                    ScanFunction(function.Function, inThisScope: false, ownName: function.Function.Id?.Name);
                    return;
                case ArrowFunctionExpression arrow:
                    ScanFunction(arrow.Function, inThisScope, ownName: null);
                    return;
                case ClassExpression classExpression:
                    ScanClass(classExpression.Class, inThisScope);
                    return;
                case UnaryExpression unary:
                    Scan(unary.Argument, inThisScope);
                    return;
                case UpdateExpression update:
                    Scan(update.Argument, inThisScope);
                    return;
                case BinaryExpression or LogicalExpression or CallExpression or MemberExpression:
                    ScanLeftNestedExpression(node, inThisScope);
                    return;
                case AssignmentExpression assignment:
                    Scan(assignment.Left, inThisScope);
                    Scan(assignment.Right, inThisScope);
                    return;
                case ConditionalExpression conditional:
                    Scan(conditional.Test, inThisScope);
                    Scan(conditional.Consequent, inThisScope);
                    Scan(conditional.Alternate, inThisScope);
                    return;
                case NewExpression newExpression:
                    Scan(newExpression.Callee, inThisScope);
                    ScanList(newExpression.Arguments, inThisScope);
                    return;
                case ChainExpression chain:
                    Scan(chain.Expression, inThisScope);
                    return;
                case SequenceExpression sequence:
                    ScanList(sequence.Expressions, inThisScope);
                    return;
                case YieldExpression yieldExpression:
                    Scan(yieldExpression.Argument, inThisScope);
                    return;
                case AwaitExpression awaitExpression:
                    Scan(awaitExpression.Argument, inThisScope);
                    return;
                case ImportExpression importExpression:
                    Scan(importExpression.Source, inThisScope);
                    Scan(importExpression.Options, inThisScope);
                    return;
                default:
                    throw new InvalidOperationException("Unexpected node in a function: " + node.Type);
            }
        }
    }
}
