using Meziantou.Framework.JavaScriptEngine.Ast;

namespace Meziantou.Framework.JavaScriptEngine.Compiler;

/// <summary>
/// Builds the scopes of a program and resolves its references, in two passes: the first creates the scopes and declares
/// the bindings (hoisting, Annex B); the second resolves the references, marks the captured bindings (used by another
/// function, or visible to eval and with) and allocates the registers and the environment slots.
/// </summary>
internal sealed partial class ScopeAnalyzer
{
    private readonly List<PendingReference> _pending = [];
    private readonly List<Scope> _allScopes = [];
    private readonly ProgramKind _programKind;

    private ScopeAnalyzer(ProgramKind programKind)
    {
        _programKind = programKind;
    }

    /// <summary>Gets the scope created for a node (a function, a block, a loop, a catch clause, a switch, a class, a with statement).</summary>
    public Dictionary<Node, Scope> Scopes { get; } = [];

    /// <summary>Gets the scope of the iteration bindings of a for-in/of statement during the evaluation of its expression (TDZ).</summary>
    public Dictionary<Node, Scope> TdzScopes { get; } = [];

    public Dictionary<Node, FunctionInfo> Functions { get; } = [];

    /// <summary>Gets the resolution of the identifiers (and of this, new.target, super, private names).</summary>
    public Dictionary<Node, Reference> References { get; } = [];

    public FunctionInfo Root { get; private set; } = null!;

    /// <summary>Gets the context of the direct eval call whose code is analyzed (null for the other programs).</summary>
    public EvalContext? EvalContext { get; private init; }

    /// <summary>Analyzes a program; its nested functions are compiled on their first call (except <paramref name="eagerFunction"/>).</summary>
    public static ScopeAnalyzer Analyze(Program program, ProgramKind kind, bool isStrict, FunctionNode? eagerFunction = null, EvalContext? evalContext = null)
    {
        var analyzer = new ScopeAnalyzer(kind) { _eagerFunction = eagerFunction, EvalContext = evalContext };

        // About one reference per 10 characters of source: the lists do not grow from scratch
        analyzer._pending.EnsureCapacity(Math.Max(16, (program.End - program.Start) / 10));
        analyzer.AnalyzeProgram(program, isStrict);
        return analyzer;
    }

    private readonly record struct PendingReference(Node Node, string Name, Scope Scope, ReferenceKind Kind);

    // ------------------------------------------------------------------------------------------------------------------
    // Pass 1: scopes and declarations
    // ------------------------------------------------------------------------------------------------------------------

    private void AnalyzeProgram(Program program, bool isStrict)
    {
        var kind = _programKind switch
        {
            ProgramKind.Module => FunctionInfoKind.Module,
            ProgramKind.Eval => FunctionInfoKind.Eval,
            _ => FunctionInfoKind.Script,
        };

        var root = new FunctionInfo(parent: null, program, kind, isStrict || program.IsStrict || program.IsModule);
        Root = root;
        Functions[program] = root;
        var scopeKind = _programKind switch
        {
            ProgramKind.Module => ScopeKind.Module,
            ProgramKind.Eval => ScopeKind.Eval,
            _ => ScopeKind.Script,
        };

        var scope = CreateScope(scopeKind, parent: null, root, program);
        root.Scope = scope;

        // Var-scoped declarations (var, top-level functions), then the lexical declarations of the top level
        foreach (var statement in program.Body)
        {
            DeclareVarScoped(statement, scope, isTopLevel: true);
        }

        DeclareLexical(program.Body, scope, isFunctionTopLevel: true);
        if (_programKind == ProgramKind.Eval && !root.IsStrict)
        {
            // The vars and functions of sloppy eval code are created in the variable environment of the caller
            foreach (var variable in scope.OrderedVariables)
            {
                if (variable.Kind is VariableKind.Var or VariableKind.Function)
                {
                    variable.Kind = variable.Kind == VariableKind.Function ? VariableKind.Function : VariableKind.EvalVar;
                    variable.Location = VariableLocation.Dynamic;
                }
            }
        }

        VisitStatements(program.Body, scope);
        ResolveAll();
    }

    private Scope CreateScope(ScopeKind kind, Scope? parent, FunctionInfo function, Node? node)
    {
        var scope = new Scope(kind, parent, function, node);
        if (node is not null)
        {
            Scopes[node] = scope;
        }

        _allScopes.Add(scope);
        return scope;
    }

    /// <summary>Declares the VarScopedDeclarations of a statement (and, at the top level of a function or script, the function declarations).</summary>
    private void DeclareVarScoped(Node? statement, Scope varScope, bool isTopLevel)
    {
        CompilerStack.EnsureSufficientStack();
        switch (statement)
        {
            case null:
                return;
            case VariableDeclaration { Kind: VariableDeclarationKind.Var } declaration:
                foreach (var declarator in declaration.Declarations)
                {
                    foreach (var name in GetBoundNames(declarator.Id))
                    {
                        DeclareVar(varScope, name.Name, VariableKind.Var);
                    }
                }

                break;
            case FunctionDeclaration function when isTopLevel:
                DeclareVar(varScope, function.Function.Id!.Name, VariableKind.Function);
                break;
            case BlockStatement block:
                foreach (var child in block.Body)
                {
                    DeclareVarScoped(child, varScope, isTopLevel: false);
                }

                break;
            case IfStatement ifStatement:
                DeclareVarScoped(ifStatement.Consequent, varScope, isTopLevel: false);
                DeclareVarScoped(ifStatement.Alternate, varScope, isTopLevel: false);
                break;
            case WhileStatement whileStatement:
                DeclareVarScoped(whileStatement.Body, varScope, isTopLevel: false);
                break;
            case DoWhileStatement doWhile:
                DeclareVarScoped(doWhile.Body, varScope, isTopLevel: false);
                break;
            case ForStatement forStatement:
                DeclareVarScoped(forStatement.Init, varScope, isTopLevel: false);
                DeclareVarScoped(forStatement.Body, varScope, isTopLevel: false);
                break;
            case ForInStatement forIn:
                DeclareVarScoped(forIn.Left, varScope, isTopLevel: false);
                DeclareVarScoped(forIn.Body, varScope, isTopLevel: false);
                break;
            case ForOfStatement forOf:
                DeclareVarScoped(forOf.Left, varScope, isTopLevel: false);
                DeclareVarScoped(forOf.Body, varScope, isTopLevel: false);
                break;
            case LabeledStatement labeled:
                // A labelled function declaration at the top level is a top-level function (Annex B)
                DeclareVarScoped(labeled.Body, varScope, isTopLevel && labeled.Body is FunctionDeclaration or LabeledStatement);
                break;
            case SwitchStatement switchStatement:
                foreach (var switchCase in switchStatement.Cases)
                {
                    foreach (var child in switchCase.Consequent)
                    {
                        DeclareVarScoped(child, varScope, isTopLevel: false);
                    }
                }

                break;
            case TryStatement tryStatement:
                DeclareVarScoped(tryStatement.Block, varScope, isTopLevel: false);
                DeclareVarScoped(tryStatement.Handler?.Body, varScope, isTopLevel: false);
                DeclareVarScoped(tryStatement.Finalizer, varScope, isTopLevel: false);
                break;
            case WithStatement withStatement:
                DeclareVarScoped(withStatement.Body, varScope, isTopLevel: false);
                break;
            case ExportNamedDeclaration { Declaration: { } exported }:
                DeclareVarScoped(exported, varScope, isTopLevel);
                break;
            case ExportDefaultDeclaration { Declaration: FunctionDeclaration exportedFunction } when isTopLevel:
                DeclareVar(varScope, exportedFunction.Function.Id?.Name ?? "*default*", VariableKind.Function);
                break;
        }
    }

    private static Variable DeclareVar(Scope varScope, string name, VariableKind kind)
    {
        if (varScope.Find(name) is { } existing)
        {
            if (kind == VariableKind.Function && existing.Kind == VariableKind.Var)
            {
                existing.Kind = VariableKind.Function;
            }

            return existing;
        }

        return varScope.Declare(name, kind);
    }

    /// <summary>Declares the LexicallyScopedDeclarations of a statement list in a scope.</summary>
    private void DeclareLexical(List<Node> statements, Scope scope, bool isFunctionTopLevel)
    {
        foreach (var statement in statements)
        {
            DeclareLexicalStatement(statement, scope, isFunctionTopLevel);
        }
    }

    private void DeclareLexicalStatement(Node statement, Scope scope, bool isFunctionTopLevel)
    {
        CompilerStack.EnsureSufficientStack();
        switch (statement)
        {
            case VariableDeclaration { Kind: not VariableDeclarationKind.Var } declaration:
                foreach (var declarator in declaration.Declarations)
                {
                    foreach (var name in GetBoundNames(declarator.Id))
                    {
                        var variable = scope.Declare(name.Name, declaration.Kind == VariableDeclarationKind.Let ? VariableKind.Let : VariableKind.Const);
                        variable.DeclarationEnd = declarator.End;
                        variable.IsUsing = declaration.Kind is VariableDeclarationKind.Using or VariableDeclarationKind.AwaitUsing;
                        variable.IsAwaitUsing = declaration.Kind is VariableDeclarationKind.AwaitUsing;
                    }
                }

                break;
            case ClassDeclaration classDeclaration:
                var classVariable = scope.Declare(classDeclaration.Class.Id!.Name, VariableKind.Class);
                classVariable.DeclarationEnd = classDeclaration.End;
                break;
            case FunctionDeclaration function when !isFunctionTopLevel:
                if (scope.Find(function.Function.Id!.Name) is null)
                {
                    scope.Declare(function.Function.Id.Name, VariableKind.BlockFunction);
                }
                break;
            case LabeledStatement labeled when !isFunctionTopLevel:
                DeclareLexicalStatement(labeled.Body, scope, isFunctionTopLevel);
                break;
            case ExportNamedDeclaration { Declaration: { } exported }:
                DeclareLexicalStatement(exported, scope, isFunctionTopLevel);
                break;
            case ExportDefaultDeclaration exportDefault:
                if (exportDefault.Declaration is ClassDeclaration exportedClass)
                {
                    var variable = scope.Declare(exportedClass.Class.Id?.Name ?? "*default*", VariableKind.Class);
                    variable.DeclarationEnd = exportDefault.End;
                }
                else if (exportDefault.Declaration is not FunctionDeclaration)
                {
                    var variable = scope.Declare("*default*", VariableKind.Const);
                    variable.DeclarationEnd = exportDefault.End;
                }

                break;
            case ImportDeclaration import:
                foreach (var specifier in import.Specifiers)
                {
                    var local = specifier switch
                    {
                        ImportSpecifier s => s.Local,
                        ImportDefaultSpecifier s => s.Local,
                        ImportNamespaceSpecifier s => s.Local,
                        _ => throw new InvalidOperationException("Unknown import specifier"),
                    };

                    // A namespace import is an ordinary immutable binding (initialized during the instantiation)
                    scope.Declare(local.Name, specifier is ImportNamespaceSpecifier ? VariableKind.Const : VariableKind.Import).DeclarationEnd = 0;
                }

                break;
        }
    }

    /// <summary>BoundNames of a binding target (identifier or pattern).</summary>
    public static IEnumerable<Identifier> GetBoundNames(Node? target)
    {
        switch (target)
        {
            case Identifier identifier:
                yield return identifier;
                break;
            case ObjectPattern objectPattern:
                foreach (var property in objectPattern.Properties)
                {
                    var value = property is Property p ? p.Value : ((RestElement)property).Argument;
                    foreach (var name in GetBoundNames(value))
                        yield return name;
                }

                break;
            case ArrayPattern arrayPattern:
                foreach (var element in arrayPattern.Elements)
                {
                    foreach (var name in GetBoundNames(element))
                        yield return name;
                }

                break;
            case AssignmentPattern assignment:
                foreach (var name in GetBoundNames(assignment.Left))
                    yield return name;

                break;
            case RestElement rest:
                foreach (var name in GetBoundNames(rest.Argument))
                    yield return name;

                break;
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Functions and classes
    // ------------------------------------------------------------------------------------------------------------------

    private FunctionInfo VisitFunction(FunctionNode function, Scope parentScope, FunctionInfoKind kind)
    {
        // A named function expression has the scope of its name between the outer scope and the function scope
        var outer = parentScope;
        if (kind is FunctionInfoKind.Normal && function.Id is not null && IsFunctionExpression(function))
        {
            outer = CreateFunctionNameScope(function, parentScope);
        }

        var info = new FunctionInfo(parentScope.Function, function, kind, function.IsStrict);
        parentScope.Function.Children.Add(info);
        Functions[function] = info;
        var scope = CreateScope(ScopeKind.Function, outer, info, function);
        info.Scope = scope;

        // Parameters
        var hasParameterExpressions = !function.HasSimpleParameterList && function.Params.Exists(ContainsExpression);
        info.HasParameterExpressions = hasParameterExpressions;
        foreach (var parameter in function.Params)
        {
            foreach (var name in GetBoundNames(parameter))
            {
                var variable = scope.Find(name.Name) ?? scope.Declare(name.Name, VariableKind.Parameter);
                if (hasParameterExpressions)
                {
                    variable.IsParameterWithTdz = true;
                    variable.DeclarationEnd = parameter.End;
                }
            }
        }

        // The variable environment of the body is separate when the parameters have expressions
        var varScope = scope;
        if (hasParameterExpressions)
        {
            varScope = CreateScope(ScopeKind.FunctionBody, scope, info, node: null);
            info.BodyVarScope = varScope;
        }

        var body = function.Body as BlockStatement;
        if (body is not null)
        {
            foreach (var statement in body.Body)
            {
                DeclareVarScoped(statement, varScope, isTopLevel: true);
            }
        }

        // The arguments object (FunctionDeclarationInstantiation steps 15-22)
        if (kind is not FunctionInfoKind.Arrow and not FunctionInfoKind.ClassFieldInitializer and not FunctionInfoKind.ClassStaticBlock)
        {
            var argumentsNeeded = scope.Find("arguments") is null || scope.Find("arguments")!.Kind != VariableKind.Parameter;
            if (argumentsNeeded && !hasParameterExpressions && varScope.Find("arguments") is { Kind: VariableKind.Function })
            {
                argumentsNeeded = false;
            }

            if (argumentsNeeded && !hasParameterExpressions && body is not null && HasLexicalDeclaration(body.Body, "arguments"))
            {
                argumentsNeeded = false;
            }

            if (argumentsNeeded)
            {
                info.ArgumentsVariable = scope.Find("arguments") ?? scope.Declare("arguments", VariableKind.Arguments);
                info.UsesMappedArguments = !function.IsStrict && function.HasSimpleParameterList;
            }
        }

        // The lexical declarations of the top level of the body
        if (body is not null)
        {
            var lexicalScope = CreateScope(ScopeKind.Block, varScope, info, node: null);
            info.LexicalScope = lexicalScope;
            DeclareLexical(body.Body, lexicalScope, isFunctionTopLevel: true);
        }

        // Visit the parameters (their expressions run in the function scope) and the body
        foreach (var parameter in function.Params)
        {
            VisitPattern(parameter, scope, isDeclaration: true);
        }

        if (body is not null)
        {
            VisitStatements(body.Body, info.LexicalScope!);
        }
        else
        {
            VisitExpression(function.Body, scope);
        }

        return info;
    }

    private readonly Dictionary<FunctionNode, Scope> _functionNameScopes = [];
    private readonly HashSet<FunctionNode> _functionExpressions = [];

    /// <summary>Gets the scope of the name of a named function expression, if any.</summary>
    public Scope? GetFunctionNameScope(FunctionNode function) => _functionNameScopes.GetValueOrDefault(function);

    private bool IsFunctionExpression(FunctionNode function) => _functionExpressions.Contains(function);

    private static bool ContainsExpression(Node parameter) => parameter is not Identifier;

    private static bool HasLexicalDeclaration(List<Node> statements, string name)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case VariableDeclaration { Kind: not VariableDeclarationKind.Var } declaration:
                    foreach (var declarator in declaration.Declarations)
                    {
                        foreach (var bound in GetBoundNames(declarator.Id))
                        {
                            if (bound.Name == name)
                                return true;
                        }
                    }

                    break;
                case ClassDeclaration classDeclaration when classDeclaration.Class.Id?.Name == name:
                    return true;
            }
        }

        return false;
    }

    private void VisitClass(ClassNode node, Scope parentScope)
    {
        var classScope = CreateScope(ScopeKind.Class, parentScope, parentScope.Function, node);
        if (node.Id is not null)
        {
            classScope.Declare(node.Id.Name, VariableKind.ClassName);
        }

        // The private names of the class
        foreach (var element in node.Body)
        {
            var key = element switch
            {
                MethodDefinition method => method.Key,
                PropertyDefinition property => property.Key,
                _ => null,
            };

            if (key is PrivateIdentifier privateName && classScope.Find("#" + privateName.Name) is null)
            {
                var variable = classScope.Declare("#" + privateName.Name, VariableKind.PrivateName);
                variable.IsCaptured = true;
            }
        }

        if (node.SuperClass is not null)
        {
            VisitExpression(node.SuperClass, classScope);
        }

        // The synthetic initializers of the fields (one for the instance fields, one for the static elements)
        FunctionInfo? instanceInitializer = null;
        FunctionInfo? staticInitializer = null;
        var computedKeyIndex = 0;
        foreach (var element in node.Body)
        {
            switch (element)
            {
                case MethodDefinition method:
                    if (method.Computed)
                    {
                        VisitExpression(method.Key, classScope);
                    }

                    var methodKind = method.Kind == MethodKind.Constructor ? FunctionInfoKind.ClassConstructor : FunctionInfoKind.Method;
                    VisitNestedFunction(method.Value, classScope, methodKind);
                    break;
                case PropertyDefinition property:
                    if (property.Computed)
                    {
                        VisitExpression(property.Key, classScope);

                        // The value of the key is computed when the class is defined, and read by the initializer
                        var hidden = classScope.Declare("%key" + computedKeyIndex.ToString(System.Globalization.CultureInfo.InvariantCulture), VariableKind.Hidden);
                        hidden.IsCaptured = true;
                        _computedFieldKeys[property] = hidden;
                        computedKeyIndex++;
                    }

                    var initializer = property.Static
                        ? staticInitializer ??= CreateClassInitializer(node, classScope, FunctionInfoKind.ClassStaticInitializer)
                        : instanceInitializer ??= CreateClassInitializer(node, classScope, FunctionInfoKind.ClassFieldInitializer);
                    if (property.Value is not null)
                    {
                        // Each initializer is a separate method for eval and the anonymous function names
                        VisitExpression(property.Value, initializer.LexicalScope!);
                    }

                    break;
                case StaticBlock staticBlock:
                    staticInitializer ??= CreateClassInitializer(node, classScope, FunctionInfoKind.ClassStaticInitializer);
                    VisitStaticBlock(staticBlock, staticInitializer.LexicalScope!);
                    break;
            }
        }

        if (instanceInitializer is not null)
        {
            _classInitializers[(node, false)] = instanceInitializer;
        }

        if (staticInitializer is not null)
        {
            _classInitializers[(node, true)] = staticInitializer;
        }
    }

    private readonly Dictionary<PropertyDefinition, Variable> _computedFieldKeys = [];
    private readonly Dictionary<(ClassNode Class, bool Static), FunctionInfo> _classInitializers = [];

    /// <summary>Gets the hidden binding that holds the computed key of a field.</summary>
    public Variable GetComputedFieldKey(PropertyDefinition property) => _computedFieldKeys[property];

    /// <summary>Gets the synthetic initializer of the instance fields (static: false) or of the static elements (static: true).</summary>
    public FunctionInfo? GetClassInitializer(ClassNode node, bool isStatic) => _classInitializers.GetValueOrDefault((node, isStatic));

    private FunctionInfo CreateClassInitializer(ClassNode node, Scope classScope, FunctionInfoKind kind)
    {
        var info = new FunctionInfo(classScope.Function, node, kind, isStrict: true);
        classScope.Function.Children.Add(info);
        var scope = CreateScope(ScopeKind.Function, classScope, info, node: null);
        info.Scope = scope;
        info.LexicalScope = CreateScope(ScopeKind.Block, scope, info, node: null);
        return info;
    }

    private void VisitStaticBlock(StaticBlock block, Scope initializerScope)
    {
        // A static block is a method-like function called with the class as this (its vars are local)
        var info = new FunctionInfo(initializerScope.Function, block, FunctionInfoKind.ClassStaticBlock, isStrict: true);
        initializerScope.Function.Children.Add(info);
        Functions[block] = info;
        var scope = CreateScope(ScopeKind.Function, initializerScope, info, block);
        info.Scope = scope;
        foreach (var statement in block.Body)
        {
            DeclareVarScoped(statement, scope, isTopLevel: true);
        }

        var lexicalScope = CreateScope(ScopeKind.Block, scope, info, node: null);
        info.LexicalScope = lexicalScope;
        DeclareLexical(block.Body, lexicalScope, isFunctionTopLevel: true);
        VisitStatements(block.Body, lexicalScope);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Annex B.3.2: function declarations in blocks of sloppy code
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>B.3.2.1/B.3.2.2: whether a block function also creates a var binding of the same name in the variable environment.</summary>
    private void CheckAnnexBFunction(FunctionDeclaration declaration, Scope blockScope)
    {
        var function = blockScope.Function;
        if (function.IsStrict || declaration.Function.IsAsync || declaration.Function.IsGenerator)
            return;

        var name = declaration.Function.Id!.Name;

        // Replacing the declaration by "var F" must not produce an early error: no lexical declaration of F in the
        // enclosing scopes up to the variable environment (the block itself only has the function as F)
        var varScope = function.BodyVarScope ?? function.Scope;
        for (var scope = blockScope.Parent; scope is not null && scope != varScope; scope = scope.Parent)
        {
            if (scope.Function != function)
                return;

            if (scope.Find(name) is { } variable)
            {
                // B.3.4: a var may redeclare a simple catch parameter
                if (variable.Kind == VariableKind.CatchParameter && scope.Node is CatchClause { Param: Identifier })
                    continue;

                if (variable.Kind != VariableKind.Var)
                    return;
            }
        }

        // The variable environment can also hold the lexical declarations (eval code, scripts)
        if (varScope.Find(name) is { Kind: VariableKind.Let or VariableKind.Const or VariableKind.Class })
            return;

        // F is not a parameter name (B.3.2.1)
        if (function.Kind != FunctionInfoKind.Script && function.Kind != FunctionInfoKind.Eval && function.Scope.Find(name) is { Kind: VariableKind.Parameter })
            return;

        // "arguments" is one of the parameter names when the function has an arguments object
        if (name == "arguments" && function.ArgumentsVariable is not null)
            return;

        if (varScope.Find(name) is null)
        {
            var variable = varScope.Declare(name, VariableKind.Var);
            if (function.Kind == FunctionInfoKind.Eval)
            {
                variable.Kind = VariableKind.EvalVar;
                variable.Location = VariableLocation.Dynamic;
            }
        }

        function.AnnexBFunctions.Add(declaration);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Statements
    // ------------------------------------------------------------------------------------------------------------------

    private void VisitStatements(List<Node> statements, Scope scope)
    {
        foreach (var statement in statements)
        {
            VisitStatement(statement, scope);
        }
    }

    private void VisitStatement(Node? node, Scope scope)
    {
        CompilerStack.EnsureSufficientStack();
        switch (node)
        {
            case null:
            case EmptyStatement:
            case DebuggerStatement:
            case BreakStatement:
            case ContinueStatement:
                return;
            case ExpressionStatement statement:
                VisitExpression(statement.Expression, scope);
                return;
            case VariableDeclaration declaration:
                foreach (var declarator in declaration.Declarations)
                {
                    VisitPattern(declarator.Id, scope, isDeclaration: true);
                    if (declarator.Init is not null)
                    {
                        VisitExpression(declarator.Init, scope);
                    }
                }

                return;
            case FunctionDeclaration declaration:
                if (!IsTopLevelOf(scope))
                {
                    CheckAnnexBFunction(declaration, scope);
                }

                RecordReference(declaration.Function.Id!, declaration.Function.Id!.Name, scope, ReferenceKind.Declaration);
                VisitNestedFunction(declaration.Function, scope, FunctionInfoKind.Normal);
                return;
            case ClassDeclaration declaration:
                RecordReference(declaration.Class.Id!, declaration.Class.Id!.Name, scope, ReferenceKind.Declaration);
                VisitClass(declaration.Class, scope);
                return;
            case BlockStatement block:
                {
                    var blockScope = CreateScope(ScopeKind.Block, scope, scope.Function, block);
                    DeclareLexical(block.Body, blockScope, isFunctionTopLevel: false);
                    VisitStatements(block.Body, blockScope);
                    return;
                }

            case IfStatement ifStatement:
                VisitExpression(ifStatement.Test, scope);
                VisitSubStatement(ifStatement.Consequent, scope);
                VisitSubStatement(ifStatement.Alternate, scope);
                return;
            case LabeledStatement labeled:
                // A labelled function declaration belongs to the enclosing statement list
                VisitStatement(labeled.Body, scope);
                return;
            case WhileStatement whileStatement:
                VisitExpression(whileStatement.Test, scope);
                VisitStatement(whileStatement.Body, scope);
                return;
            case DoWhileStatement doWhile:
                VisitStatement(doWhile.Body, scope);
                VisitExpression(doWhile.Test, scope);
                return;
            case ForStatement forStatement:
                {
                    var loopScope = scope;
                    if (forStatement.Init is VariableDeclaration { Kind: not VariableDeclarationKind.Var } lexical)
                    {
                        loopScope = CreateScope(ScopeKind.Block, scope, scope.Function, forStatement);
                        DeclareLexicalStatement(lexical, loopScope, isFunctionTopLevel: false);
                    }

                    if (forStatement.Init is VariableDeclaration)
                    {
                        VisitStatement(forStatement.Init, loopScope);
                    }
                    else if (forStatement.Init is not null)
                    {
                        VisitExpression(forStatement.Init, loopScope);
                    }

                    if (forStatement.Test is not null)
                    {
                        VisitExpression(forStatement.Test, loopScope);
                    }

                    if (forStatement.Update is not null)
                    {
                        VisitExpression(forStatement.Update, loopScope);
                    }

                    VisitStatement(forStatement.Body, loopScope);
                    return;
                }

            case ForInStatement forIn:
                VisitForInOf(forIn, forIn.Left, forIn.Right, forIn.Body, scope);
                return;
            case ForOfStatement forOf:
                VisitForInOf(forOf, forOf.Left, forOf.Right, forOf.Body, scope);
                return;
            case ReturnStatement returnStatement:
                if (returnStatement.Argument is not null)
                {
                    VisitExpression(returnStatement.Argument, scope);
                }

                return;
            case ThrowStatement throwStatement:
                VisitExpression(throwStatement.Argument, scope);
                return;
            case WithStatement withStatement:
                {
                    VisitExpression(withStatement.Object, scope);
                    var withScope = CreateScope(ScopeKind.With, scope, scope.Function, withStatement);
                    withScope.NeedsContext = true;
                    VisitStatement(withStatement.Body, withScope);
                    return;
                }

            case SwitchStatement switchStatement:
                {
                    VisitExpression(switchStatement.Discriminant, scope);
                    var switchScope = CreateScope(ScopeKind.Switch, scope, scope.Function, switchStatement);
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        DeclareLexical(switchCase.Consequent, switchScope, isFunctionTopLevel: false);
                    }

                    foreach (var switchCase in switchStatement.Cases)
                    {
                        if (switchCase.Test is not null)
                        {
                            VisitExpression(switchCase.Test, switchScope);
                        }

                        VisitStatements(switchCase.Consequent, switchScope);
                    }

                    return;
                }

            case TryStatement tryStatement:
                VisitStatement(tryStatement.Block, scope);
                if (tryStatement.Handler is { } handler)
                {
                    var catchScope = CreateScope(ScopeKind.Catch, scope, scope.Function, handler);
                    if (handler.Param is not null)
                    {
                        foreach (var name in GetBoundNames(handler.Param))
                        {
                            catchScope.Declare(name.Name, VariableKind.CatchParameter);
                        }

                        VisitPattern(handler.Param, catchScope, isDeclaration: true);
                    }

                    VisitStatement(handler.Body, catchScope);
                }

                VisitStatement(tryStatement.Finalizer, scope);
                return;
            case ImportDeclaration import:
                foreach (var specifier in import.Specifiers)
                {
                    var local = specifier switch
                    {
                        ImportSpecifier s => s.Local,
                        ImportDefaultSpecifier s => s.Local,
                        ImportNamespaceSpecifier s => s.Local,
                        _ => throw new InvalidOperationException("Unknown import specifier"),
                    };

                    RecordReference(local, local.Name, scope, ReferenceKind.Declaration);
                }

                return;
            case ExportNamedDeclaration export:
                if (export.Declaration is not null)
                {
                    VisitStatement(export.Declaration, scope);
                }
                else if (export.Source is null)
                {
                    foreach (var specifier in export.Specifiers)
                    {
                        if (specifier.Local is Identifier local)
                        {
                            RecordReference(local, local.Name, scope, ReferenceKind.Read);
                        }
                    }
                }

                return;
            case ExportDefaultDeclaration exportDefault:
                switch (exportDefault.Declaration)
                {
                    case FunctionDeclaration function:
                        if (function.Function.Id is not null)
                        {
                            RecordReference(function.Function.Id, function.Function.Id.Name, scope, ReferenceKind.Declaration);
                        }

                        VisitNestedFunction(function.Function, scope, FunctionInfoKind.Normal);
                        break;
                    case ClassDeclaration classDeclaration:
                        if (classDeclaration.Class.Id is not null)
                        {
                            RecordReference(classDeclaration.Class.Id, classDeclaration.Class.Id.Name, scope, ReferenceKind.Declaration);
                        }

                        VisitClass(classDeclaration.Class, scope);
                        break;
                    default:
                        VisitExpression(exportDefault.Declaration, scope);
                        break;
                }

                return;
            case ExportAllDeclaration:
                return;
            default:
                throw new InvalidOperationException("Unexpected statement: " + node.Type);
        }
    }

    private static bool IsTopLevelOf(Scope scope)
    {
        // The top-level function declarations are declared in the lexical scope of the top level of the function
        return scope == scope.Function.LexicalScope || scope == scope.Function.Scope;
    }

    /// <summary>Visits the statement of an if/label: a function declaration there (Annex B) is in its own block scope.</summary>
    private void VisitSubStatement(Node? statement, Scope scope)
    {
        if (statement is FunctionDeclaration function)
        {
            var blockScope = CreateScope(ScopeKind.Block, scope, scope.Function, node: null);
            _implicitBlockScopes[function] = blockScope;
            blockScope.Declare(function.Function.Id!.Name, VariableKind.BlockFunction);
            VisitStatement(function, blockScope);
            return;
        }

        VisitStatement(statement, scope);
    }

    private readonly Dictionary<FunctionDeclaration, Scope> _implicitBlockScopes = [];

    /// <summary>Gets the implicit block scope of a function declaration that is the statement of an if or a label (Annex B.3.3).</summary>
    public Scope? GetImplicitBlockScope(FunctionDeclaration declaration) => _implicitBlockScopes.GetValueOrDefault(declaration);

    private void VisitForInOf(Node node, Node left, Node right, Node body, Scope scope)
    {
        if (left is VariableDeclaration { Kind: not VariableDeclarationKind.Var } lexical)
        {
            // The expression is evaluated with the bound names in TDZ
            var tdzScope = CreateScope(ScopeKind.Block, scope, scope.Function, node: null);
            TdzScopes[node] = tdzScope;
            foreach (var name in GetBoundNames(lexical.Declarations[0].Id))
            {
                tdzScope.Declare(name.Name, VariableKind.Let).DeclarationEnd = int.MaxValue;
            }

            VisitExpression(right, tdzScope);
            var iterationScope = CreateScope(ScopeKind.Block, scope, scope.Function, node);
            DeclareLexicalStatement(lexical, iterationScope, isFunctionTopLevel: false);
            VisitPattern(lexical.Declarations[0].Id, iterationScope, isDeclaration: true);
            VisitStatement(body, iterationScope);
            return;
        }

        if (left is VariableDeclaration varDeclaration)
        {
            var declarator = varDeclaration.Declarations[0];
            VisitPattern(declarator.Id, scope, isDeclaration: true);
            if (declarator.Init is not null)
            {
                VisitExpression(declarator.Init, scope);
            }
        }
        else
        {
            VisitPattern(left, scope, isDeclaration: false);
        }

        VisitExpression(right, scope);
        VisitStatement(body, scope);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Expressions and patterns
    // ------------------------------------------------------------------------------------------------------------------

    private void VisitPattern(Node? target, Scope scope, bool isDeclaration)
    {
        CompilerStack.EnsureSufficientStack();
        switch (target)
        {
            case null:
                return;
            case Identifier identifier:
                RecordReference(identifier, identifier.Name, scope, isDeclaration ? ReferenceKind.Declaration : ReferenceKind.Write);
                return;
            case ObjectPattern objectPattern:
                foreach (var property in objectPattern.Properties)
                {
                    if (property is Property p)
                    {
                        if (p.Computed)
                        {
                            VisitExpression(p.Key, scope);
                        }

                        VisitPattern(p.Value, scope, isDeclaration);
                    }
                    else
                    {
                        VisitPattern(((RestElement)property).Argument, scope, isDeclaration);
                    }
                }

                return;
            case ArrayPattern arrayPattern:
                foreach (var element in arrayPattern.Elements)
                {
                    VisitPattern(element, scope, isDeclaration);
                }

                return;
            case AssignmentPattern assignment:
                VisitPattern(assignment.Left, scope, isDeclaration);
                VisitExpression(assignment.Right, scope);
                return;
            case RestElement rest:
                VisitPattern(rest.Argument, scope, isDeclaration);
                return;
            default:
                // A member expression in an assignment pattern
                VisitExpression(target, scope);
                return;
        }
    }

    private void VisitExpression(Node? node, Scope scope)
    {
        CompilerStack.EnsureSufficientStack();
        switch (node)
        {
            case null:
            case NullLiteral:
            case BooleanLiteral:
            case NumericLiteral:
            case BigIntLiteral:
            case StringLiteral:
            case RegExpLiteral:
                return;
            case Identifier identifier:
                RecordReference(identifier, identifier.Name, scope, ReferenceKind.Read);
                return;
            case ThisExpression:
                RecordSpecialReference(node, scope, VariableKind.This);
                return;
            case Super:
                RecordSpecialReference(node, scope, VariableKind.HomeObject);
                return;
            case MetaProperty meta:
                if (meta.Kind == MetaPropertyKind.NewTarget)
                {
                    RecordSpecialReference(node, scope, VariableKind.NewTarget);
                }

                return;
            case TemplateLiteral template:
                foreach (var expression in template.Expressions)
                {
                    VisitExpression(expression, scope);
                }

                return;
            case TaggedTemplateExpression tagged:
                VisitExpression(tagged.Tag, scope);
                VisitExpression(tagged.Quasi, scope);
                return;
            case ArrayExpression array:
                foreach (var element in array.Elements)
                {
                    VisitExpression(element, scope);
                }

                return;
            case ObjectExpression obj:
                foreach (var property in obj.Properties)
                {
                    if (property is Property p)
                    {
                        if (p.Computed)
                        {
                            VisitExpression(p.Key, scope);
                        }

                        if (p.Value is FunctionExpression method && (p.Method || p.Kind != PropertyKind.Init))
                        {
                            VisitNestedFunction(method.Function, scope, FunctionInfoKind.Method);
                        }
                        else
                        {
                            VisitExpression(p.Value, scope);
                        }
                    }
                    else
                    {
                        VisitExpression(((SpreadElement)property).Argument, scope);
                    }
                }

                return;
            case SpreadElement spread:
                VisitExpression(spread.Argument, scope);
                return;
            case FunctionExpression function:
                _functionExpressions.Add(function.Function);
                VisitNestedFunction(function.Function, scope, FunctionInfoKind.Normal);
                return;
            case ArrowFunctionExpression arrow:
                VisitNestedFunction(arrow.Function, scope, FunctionInfoKind.Arrow);
                return;
            case ClassExpression classExpression:
                VisitClass(classExpression.Class, scope);
                return;
            case UnaryExpression unary:
                VisitExpression(unary.Argument, scope);
                return;
            case UpdateExpression update:
                VisitAssignmentTarget(update.Argument, scope, isReadWrite: true);
                return;
            case BinaryExpression binary:
                if (binary.Left is PrivateIdentifier privateIdentifier)
                {
                    RecordReference(privateIdentifier, "#" + privateIdentifier.Name, scope, ReferenceKind.Read);
                }
                else
                {
                    VisitExpression(binary.Left, scope);
                }

                VisitExpression(binary.Right, scope);
                return;
            case LogicalExpression logical:
                VisitExpression(logical.Left, scope);
                VisitExpression(logical.Right, scope);
                return;
            case AssignmentExpression assignment:
                if (assignment.Operator == AssignmentOperator.Assign && assignment.Left is ObjectPattern or ArrayPattern)
                {
                    VisitPattern(assignment.Left, scope, isDeclaration: false);
                }
                else
                {
                    VisitAssignmentTarget(assignment.Left, scope, isReadWrite: assignment.Operator != AssignmentOperator.Assign);
                }

                VisitExpression(assignment.Right, scope);
                return;
            case ConditionalExpression conditional:
                VisitExpression(conditional.Test, scope);
                VisitExpression(conditional.Consequent, scope);
                VisitExpression(conditional.Alternate, scope);
                return;
            case CallExpression call:
                if (call.Callee is Identifier { Name: "eval" } && !call.Optional)
                {
                    MarkDirectEval(scope);
                }

                if (call.Callee is Super)
                {
                    // super(): the this binding, new.target and the active function of the derived constructor
                    RecordSpecialReference(call, scope, VariableKind.ActiveFunction);
                    RecordSpecialReference(call, scope, VariableKind.This);
                    RecordSpecialReference(call, scope, VariableKind.NewTarget);
                }
                else
                {
                    VisitExpression(call.Callee, scope);
                }

                foreach (var argument in call.Arguments)
                {
                    VisitExpression(argument, scope);
                }

                return;
            case NewExpression newExpression:
                VisitExpression(newExpression.Callee, scope);
                foreach (var argument in newExpression.Arguments)
                {
                    VisitExpression(argument, scope);
                }

                return;
            case MemberExpression member:
                if (member.Object is Super)
                {
                    RecordSpecialReference(member.Object, scope, VariableKind.HomeObject);
                    RecordSpecialReference(member, scope, VariableKind.This);
                }
                else
                {
                    VisitExpression(member.Object, scope);
                }

                if (member.Computed)
                {
                    VisitExpression(member.Property, scope);
                }
                else if (member.Property is PrivateIdentifier privateName)
                {
                    RecordReference(privateName, "#" + privateName.Name, scope, ReferenceKind.Read);
                }

                return;
            case ChainExpression chain:
                VisitExpression(chain.Expression, scope);
                return;
            case SequenceExpression sequence:
                foreach (var expression in sequence.Expressions)
                {
                    VisitExpression(expression, scope);
                }

                return;
            case YieldExpression yieldExpression:
                VisitExpression(yieldExpression.Argument, scope);
                return;
            case AwaitExpression awaitExpression:
                VisitExpression(awaitExpression.Argument, scope);
                return;
            case ImportExpression importExpression:
                VisitExpression(importExpression.Source, scope);
                VisitExpression(importExpression.Options, scope);
                return;
            case AssignmentPattern or ObjectPattern or ArrayPattern or RestElement:
                VisitPattern(node, scope, isDeclaration: false);
                return;
            default:
                throw new InvalidOperationException("Unexpected expression: " + node.Type);
        }
    }


    private void VisitAssignmentTarget(Node target, Scope scope, bool isReadWrite)
    {
        if (target is Identifier identifier)
        {
            RecordReference(identifier, identifier.Name, scope, isReadWrite ? ReferenceKind.ReadWrite : ReferenceKind.Write);
            return;
        }

        VisitExpression(target, scope);
    }

    private static void MarkDirectEval(Scope scope)
    {
        var function = scope.Function;
        function.HasDirectEval = true;
        for (var current = scope; current is not null; current = current.Parent)
        {
            current.ContainsDirectEval = true;
        }

        // Sloppy eval code adds its vars to the variable environment of the function. When the parameters have expressions,
        // the eval code of the parameters adds them to an environment outside the parameters (the parameter scope is used)
        if (!function.IsStrict)
        {
            if (function.BodyVarScope is { } bodyVarScope && !IsInScope(scope, bodyVarScope))
            {
                function.Scope.HasParameterEvalVars = true;
            }
            else
            {
                var varScope = function.BodyVarScope ?? function.Scope;
                varScope.HasSloppyEvalVars = true;
            }
        }

        static bool IsInScope(Scope scope, Scope ancestor)
        {
            for (var current = scope; current is not null && current.Function == ancestor.Function; current = current.Parent)
            {
                if (current == ancestor)
                    return true;
            }

            return false;
        }
    }

    private void RecordReference(Node node, string name, Scope scope, ReferenceKind kind) => _pending.Add(new PendingReference(node, name, scope, kind));
}
