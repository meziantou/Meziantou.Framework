using Meziantou.Framework.JavaScriptEngine.Ast;
using Meziantou.Framework.JavaScriptEngine.Text;

namespace Meziantou.Framework.JavaScriptEngine.Compiler;

/// <summary>Generates the bytecode of a function (or a script, a module, eval code) from its syntax tree and its scope analysis.</summary>
internal sealed partial class CodeGenerator
{
    private readonly ScopeAnalyzer _analysis;
    private readonly SourceText _source;
    private readonly FunctionInfo _function;
    private readonly BytecodeBuilder _builder = new();
    private Scope _scope;
    private ControlScope? _control;
    private int _completionRegister = -1;

    private CodeGenerator(ScopeAnalyzer analysis, SourceText source, FunctionInfo function)
    {
        _analysis = analysis;
        _source = source;
        _function = function;
        _scope = function.Scope;
    }

    private bool IsStrict => _function.IsStrict;

    // ------------------------------------------------------------------------------------------------------------------
    // Entry points
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>Compiles a script, a module or eval code.</summary>
    public static FunctionTemplate CompileProgram(Program program, SourceText source, ProgramKind kind, bool isStrict)
    {
        var analysis = ScopeAnalyzer.Analyze(program, kind, isStrict);
        var generator = new CodeGenerator(analysis, source, analysis.Root);
        return generator.CompileProgramBody(program, kind);
    }

    /// <summary>Compiles the function of a dynamic function (the Function constructor): its scope is the global scope.</summary>
    public static FunctionTemplate CompileDynamicFunction(FunctionNode function, SourceText source)
    {
        var program = new Program
        {
            Start = function.Start,
            End = function.End,
            Body = [new ExpressionStatement { Start = function.Start, End = function.End, Expression = new FunctionExpression { Start = function.Start, End = function.End, Function = function } }],
        };

        var analysis = ScopeAnalyzer.Analyze(program, ProgramKind.Script, isStrict: false);
        var info = analysis.Functions[function];
        var generator = new CodeGenerator(analysis, source, info);
        return generator.CompileFunctionBody(function, "anonymous", FunctionTemplateFlags.None);
    }

    /// <summary>
    /// Compiles a module: the instantiation of its function declarations (run by InitializeEnvironment, before any module
    /// of the graph is evaluated) and its body. Both run in the module environment.
    /// </summary>
    public static CompiledModule CompileModule(Program program, SourceText source)
    {
        var analysis = ScopeAnalyzer.Analyze(program, ProgramKind.Module, isStrict: true);
        var scope = analysis.Root.Scope;
        var initialization = new CodeGenerator(analysis, source, analysis.Root);
        initialization.EmitFunctionDeclarations(program.Body, scope, isGlobal: false);
        initialization._builder.Emit(OpCode.PushUndefined);
        initialization._builder.Emit(OpCode.Return);
        var initializationTemplate = initialization.Build(string.Empty, length: 0, FunctionTemplateFlags.ModuleCode | FunctionTemplateFlags.Strict, program.Start, program.End, nameScope: null);

        var body = new CodeGenerator(analysis, source, analysis.Root);
        body.EmitWithDisposal(scope, () => body.EmitStatements(program.Body));
        if (!body._builder.IsUnreachable)
        {
            body._builder.Emit(OpCode.PushUndefined);
            body._builder.Emit(OpCode.Return);
        }

        var flags = FunctionTemplateFlags.ModuleCode | FunctionTemplateFlags.Strict;
        var hasTopLevelAwait = body._builder.ContainsAwait;
        if (hasTopLevelAwait)
        {
            flags |= FunctionTemplateFlags.Async;
        }

        var bodyTemplate = body.Build(string.Empty, length: 0, flags, program.Start, program.End, nameScope: null);
        return new CompiledModule(initializationTemplate, bodyTemplate, scope.Info ?? new ScopeInfo(ScopeKind.Module, [], [], isVarScope: true), hasTopLevelAwait);
    }

    private FunctionTemplate CompileProgramBody(Program program, ProgramKind kind)
    {
        var scope = _function.Scope;
        _completionRegister = _function.AllocateRegister();
        _builder.Emit(OpCode.PushUndefined);
        _builder.Emit(OpCode.StoreLocal, _completionRegister);

        if (kind == ProgramKind.Script)
        {
            _builder.Emit(OpCode.GlobalDeclarationInstantiation, _builder.AddObject(CreateGlobalDeclarations(program, scope)));
            EmitTdzInitialization(scope);
            EmitFunctionDeclarations(program.Body, scope, isGlobal: true);
        }
        else if (kind == ProgramKind.Eval)
        {
            if (scope.NeedsContext)
            {
                _builder.Emit(OpCode.PushScope, _builder.AddObject(scope.Info!));
            }

            if (!_function.IsStrict)
            {
                _builder.Emit(OpCode.EvalDeclarationInstantiation, _builder.AddObject(CreateEvalDeclarations(program, scope)));
            }

            EmitVarInitialization(scope, parameterScope: null);
            EmitTdzInitialization(scope);
            EmitFunctionDeclarations(program.Body, scope, isGlobal: false);
        }
        else
        {
            throw new NotSupportedException("Modules are compiled by the module compiler");
        }

        EmitStatements(program.Body);
        _builder.Emit(OpCode.LoadLocal, _completionRegister);
        _builder.Emit(OpCode.Return);

        var flags = kind == ProgramKind.Eval ? FunctionTemplateFlags.EvalCode : FunctionTemplateFlags.ScriptCode;
        if (_function.IsStrict)
        {
            flags |= FunctionTemplateFlags.Strict;
        }

        return Build(string.Empty, length: 0, flags, program.Start, program.End, nameScope: null);
    }

    private FunctionTemplate Build(string name, int length, FunctionTemplateFlags flags, int sourceStart, int sourceEnd, ScopeInfo? nameScope)
    {
        var (code, constants, keys, objects, handlers, positions) = _builder.Build();
        return new FunctionTemplate
        {
            Name = name,
            Length = length,
            Flags = flags,
            Code = code,
            Constants = constants,
            Keys = keys,
            Objects = objects,
            RegisterCount = _function.RegisterCount,
            MaxStack = _builder.MaxStack + 1,
            Handlers = handlers,
            Positions = positions,
            Source = _source,
            SourceStart = sourceStart,
            SourceEnd = sourceEnd,
            NameScope = nameScope,
            CompletionRegister = _completionRegister,
            ParameterNames = _function.UsesMappedArguments && _function.FunctionNode is { } functionNode ? [.. functionNode.Params.OfType<Identifier>().Select(p => p.Name)] : null,
        };
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Functions
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>Compiles a nested function and returns the index of its template in the constants.</summary>
    private int CompileNestedFunction(FunctionNode node, string name, FunctionTemplateFlags extraFlags = FunctionTemplateFlags.None)
    {
        var info = _analysis.Functions[node];
        var generator = new CodeGenerator(_analysis, _source, info);
        var template = generator.CompileFunctionBody(node, name, extraFlags);
        return _builder.AddObject(template);
    }

    private FunctionTemplate CompileFunctionBody(FunctionNode node, string name, FunctionTemplateFlags extraFlags)
    {
        var info = _function;
        var flags = extraFlags;
        if (node.IsStrict)
        {
            flags |= FunctionTemplateFlags.Strict;
        }

        if (node.IsAsync)
        {
            flags |= FunctionTemplateFlags.Async;
        }

        if (node.IsGenerator)
        {
            flags |= FunctionTemplateFlags.Generator;
        }

        switch (info.Kind)
        {
            case FunctionInfoKind.Arrow:
                flags |= FunctionTemplateFlags.Arrow;
                break;
            case FunctionInfoKind.Method:
                flags |= FunctionTemplateFlags.Method;
                break;
            case FunctionInfoKind.ClassConstructor:
                flags |= FunctionTemplateFlags.ClassConstructor | FunctionTemplateFlags.Constructor | FunctionTemplateFlags.Method;
                if (node.IsDerivedConstructor)
                {
                    flags |= FunctionTemplateFlags.DerivedConstructor;
                }

                break;
            case FunctionInfoKind.Normal when !node.IsAsync && !node.IsGenerator:
                flags |= FunctionTemplateFlags.Constructor;
                break;
        }

        if (node.Id is null)
        {
            flags |= FunctionTemplateFlags.Anonymous;
        }

        EmitFunctionPrologue(node);
        if (node.IsGenerator)
        {
            _builder.Emit(OpCode.GeneratorStart);
        }

        if (node.IsExpressionBody)
        {
            EmitExpression(node.Body);
            _builder.Emit(OpCode.Return);
        }
        else
        {
            EmitWithDisposal(_function.LexicalScope!, () => EmitStatements(((BlockStatement)node.Body).Body));
            if (!_builder.IsUnreachable)
            {
                EmitImplicitReturn();
            }
        }

        var nameScope = _analysis.GetFunctionNameScope(node) is { NeedsContext: true } scope ? scope.Info : null;
        return Build(name, GetExpectedArgumentCount(node), flags, node.Start, node.End, nameScope);
    }

    private void EmitImplicitReturn()
    {
        _builder.Emit(OpCode.PushUndefined);
        EmitDerivedConstructorReturnValue();
        _builder.Emit(OpCode.Return);
    }

    /// <summary>ExpectedArgumentCount: the parameters before the first one with a default or a rest.</summary>
    private static int GetExpectedArgumentCount(FunctionNode node)
    {
        var count = 0;
        foreach (var parameter in node.Params)
        {
            if (parameter is AssignmentPattern or RestElement)
                break;

            count++;
        }

        return count;
    }

    /// <summary>FunctionDeclarationInstantiation (https://tc39.es/ecma262/#sec-functiondeclarationinstantiation).</summary>
    private void EmitFunctionPrologue(FunctionNode node)
    {
        var info = _function;
        var scope = info.Scope;
        _scope = scope;
        if (scope.NeedsContext)
        {
            _builder.Emit(OpCode.PushScope, _builder.AddObject(scope.Info!));
        }

        // The parameters of a function whose parameters have expressions are uninitialized until they are evaluated
        EmitTdzInitialization(scope);
        EmitSpecialBindings();

        // The arguments object
        if (info.ArgumentsVariable is { } arguments && (arguments.IsReferenced || info.HasDirectEval))
        {
            _builder.Emit(OpCode.CreateArguments, info.UsesMappedArguments ? 1 : 0);
            EmitInitializeVariable(arguments);
        }

        // The parameters, in order (their default values and patterns are evaluated in the parameter scope)
        for (var i = 0; i < node.Params.Count; i++)
        {
            var parameter = node.Params[i];
            if (parameter is RestElement rest)
            {
                _builder.Emit(OpCode.LoadRestArguments, i);
                EmitBindingInitialization(rest.Argument, isDeclaration: true);
                continue;
            }

            _builder.Emit(OpCode.LoadArgument, i);
            if (parameter is AssignmentPattern assignment)
            {
                var hasValue = _builder.DefineLabel();
                _builder.EmitJump(OpCode.JumpIfNotUndefinedKeep, hasValue);
                _builder.Emit(OpCode.Pop);
                EmitNamedEvaluation(assignment.Right, assignment.Left);
                _builder.MarkLabel(hasValue);
                EmitBindingInitialization(assignment.Left, isDeclaration: true);
            }
            else
            {
                EmitBindingInitialization(parameter, isDeclaration: true);
            }
        }

        // The variable environment of the body
        if (info.BodyVarScope is { } bodyVarScope)
        {
            EnterScope(bodyVarScope);
            EmitVarInitialization(bodyVarScope, scope);
        }
        else
        {
            EmitVarInitialization(scope, parameterScope: null);
        }

        if (info.LexicalScope is { } lexicalScope)
        {
            EnterScope(lexicalScope);
            if (node.Body is BlockStatement body)
            {
                EmitFunctionDeclarations(body.Body, info.BodyVarScope ?? scope, isGlobal: false);
            }
        }

        // The instance fields of a base class are initialized when the constructor starts
        if (info.Kind == FunctionInfoKind.ClassConstructor && !node.IsDerivedConstructor)
        {
            EmitLoadThis(node, checkInitialized: false);
            _builder.Emit(OpCode.LoadFunction);
            _builder.Emit(OpCode.InitializeInstanceElements);
            _builder.Emit(OpCode.Pop);
        }
    }

    /// <summary>Stores this, new.target, the home object and the function in the environment when nested functions or eval need them.</summary>
    private void EmitSpecialBindings()
    {
        var info = _function;
        if (info.ThisVariable is { } thisVariable)
        {
            // In a derived constructor, this is uninitialized until super() returns
            if (!(info.Kind == FunctionInfoKind.ClassConstructor && info.FunctionNode!.IsDerivedConstructor))
            {
                _builder.Emit(OpCode.LoadThis);
                EmitInitializeVariable(thisVariable);
            }
        }

        if (info.NewTargetVariable is { } newTarget)
        {
            _builder.Emit(OpCode.LoadNewTarget);
            EmitInitializeVariable(newTarget);
        }

        if (info.HomeObjectVariable is { } homeObject)
        {
            _builder.Emit(OpCode.LoadHomeObject);
            EmitInitializeVariable(homeObject);
        }

        if (info.ActiveFunctionVariable is { } activeFunction)
        {
            _builder.Emit(OpCode.LoadFunction);
            EmitInitializeVariable(activeFunction);
        }
    }

    /// <summary>Initializes the vars of a variable environment to undefined (or to the value of the parameter of the same name).</summary>
    private void EmitVarInitialization(Scope varScope, Scope? parameterScope)
    {
        foreach (var variable in varScope.OrderedVariables)
        {
            if (variable.Kind is not VariableKind.Var and not VariableKind.Function)
                continue;

            if (variable.Location is VariableLocation.Dynamic or VariableLocation.GlobalObject or VariableLocation.GlobalLexical)
                continue;

            // "var arguments" does not reset the arguments object (it is one of the instantiated names)
            if (parameterScope is null && ReferenceEquals(variable, _function.ArgumentsVariable) && (variable.IsReferenced || _function.HasDirectEval))
                continue;

            if (parameterScope?.Find(variable.Name) is { Kind: VariableKind.Parameter or VariableKind.Arguments } parameter)
            {
                EmitLoadVariable(parameter, checkTdz: false);
            }
            else
            {
                _builder.Emit(OpCode.PushUndefined);
            }

            EmitInitializeVariable(variable);
        }
    }

    /// <summary>Initializes the registers of the bindings with a TDZ to the hole (the environment slots are initialized when the environment is created).</summary>
    private void EmitTdzInitialization(Scope scope)
    {
        foreach (var variable in scope.OrderedVariables)
        {
            if (variable.Location == VariableLocation.Register && (variable.HasTdz || variable.Kind == VariableKind.BlockFunction))
            {
                _builder.Emit(OpCode.PushHole);
                _builder.Emit(OpCode.StoreLocal, variable.Index);
            }
        }
    }

    /// <summary>Instantiates the function declarations of a statement list (hoisted at the start of the scope).</summary>
    private void EmitFunctionDeclarations(List<Node> statements, Scope varScope, bool isGlobal)
    {
        foreach (var statement in statements)
        {
            var declaration = statement switch
            {
                FunctionDeclaration f => f,
                LabeledStatement labeled => GetLabeledFunction(labeled),
                ExportNamedDeclaration { Declaration: FunctionDeclaration f } => f,
                ExportDefaultDeclaration { Declaration: FunctionDeclaration f } => f,
                _ => null,
            };

            if (declaration is null)
                continue;

            var name = declaration.Function.Id?.Name ?? "default";
            var index = CompileNestedFunction(declaration.Function, name);
            _builder.Emit(OpCode.CreateClosure, index);
            if (isGlobal)
            {
                _builder.Emit(OpCode.InitializeGlobalFunction, _builder.AddKey(name));
            }
            else if (_function.Kind == FunctionInfoKind.Eval && !_function.IsStrict)
            {
                _builder.Emit(OpCode.InitializeName, _builder.AddKey(name));
            }
            else
            {
                EmitInitializeVariable(varScope.Find(declaration.Function.Id?.Name ?? "*default*")!);
            }
        }
    }

    private static FunctionDeclaration? GetLabeledFunction(LabeledStatement labeled)
    {
        Node body = labeled;
        while (body is LabeledStatement inner)
        {
            body = inner.Body;
        }

        return body as FunctionDeclaration;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Global and eval declarations
    // ------------------------------------------------------------------------------------------------------------------

    private GlobalDeclarations CreateGlobalDeclarations(Program program, Scope scope)
    {
        var lexicalNames = new List<(string Name, bool IsConstant)>();
        var varNames = new List<string>();
        var functionNames = new List<string>();
        foreach (var variable in scope.OrderedVariables)
        {
            switch (variable.Kind)
            {
                case VariableKind.Let or VariableKind.Class:
                    lexicalNames.Add((variable.Name, false));
                    break;
                case VariableKind.Const:
                    lexicalNames.Add((variable.Name, true));
                    break;
                case VariableKind.Function:
                    functionNames.Add(variable.Name);
                    break;
                case VariableKind.Var:
                    varNames.Add(variable.Name);
                    break;
            }
        }

        // The function declarations are in reverse order of their last declaration (step 10 of GlobalDeclarationInstantiation): they are created in source order by the closures
        _ = program;
        var annexBNames = _function.AnnexBFunctions.Select(f => f.Function.Id!.Name).Distinct(StringComparer.Ordinal).ToArray();
        return new GlobalDeclarations([.. lexicalNames], [.. varNames], [.. functionNames], annexBNames);
    }

    private EvalDeclarations CreateEvalDeclarations(Program program, Scope scope)
    {
        _ = program;
        var varNames = new List<string>();
        var functionNames = new List<string>();
        foreach (var variable in scope.OrderedVariables)
        {
            if (variable.Kind == VariableKind.Function)
            {
                functionNames.Add(variable.Name);
            }
            else if (variable.Kind == VariableKind.EvalVar)
            {
                varNames.Add(variable.Name);
            }
        }

        var annexBNames = _function.AnnexBFunctions.Select(f => f.Function.Id!.Name).Distinct(StringComparer.Ordinal).ToArray();
        return new EvalDeclarations([.. varNames], [.. functionNames], annexBNames);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Scopes
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>Enters a scope: pushes its environment and initializes the TDZ of its registers.</summary>
    private void EnterScope(Scope scope)
    {
        _scope = scope;
        if (scope.NeedsContext && scope.Kind != ScopeKind.With)
        {
            _builder.Emit(OpCode.PushScope, _builder.AddObject(scope.Info!));
        }

        EmitTdzInitialization(scope);
    }

    private void ExitScope(Scope scope)
    {
        if (scope.NeedsContext && !_builder.IsUnreachable)
        {
            _builder.Emit(OpCode.PopScope);
        }

        _scope = scope.Parent!;
    }

    /// <summary>The number of environments between the current scope and the scope of a binding.</summary>
    private int GetContextDepth(Scope target)
    {
        var depth = 0;
        for (var scope = _scope; scope is not null; scope = scope.Parent)
        {
            if (scope == target)
                return depth;

            if (scope.NeedsContext)
            {
                depth++;
            }
        }

        throw new InvalidOperationException($"The scope of the binding is not an enclosing scope");
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Bindings
    // ------------------------------------------------------------------------------------------------------------------

    private int GetNameKey(string name) => _builder.AddKey(name);

    private void EmitLoadVariable(Variable variable, bool checkTdz)
    {
        switch (variable.Location)
        {
            case VariableLocation.Register:
                if (checkTdz)
                {
                    _builder.Emit(OpCode.LoadLocalChecked, variable.Index, GetNameKey(variable.Name));
                }
                else
                {
                    _builder.Emit(OpCode.LoadLocal, variable.Index);
                }

                break;
            case VariableLocation.Context when variable.Kind == VariableKind.Import:
                _builder.Emit(OpCode.LoadImport, GetContextDepth(variable.Scope), variable.Index, GetNameKey(variable.Name));
                break;
            case VariableLocation.Context:
                if (checkTdz)
                {
                    _builder.Emit(OpCode.LoadContextChecked, GetContextDepth(variable.Scope), variable.Index, GetNameKey(variable.Name));
                }
                else
                {
                    _builder.Emit(OpCode.LoadContext, GetContextDepth(variable.Scope), variable.Index);
                }

                break;
            case VariableLocation.GlobalObject:
            case VariableLocation.GlobalLexical:
                _builder.Emit(OpCode.LoadGlobal, GetNameKey(variable.Name));
                break;
            default:
                _builder.Emit(OpCode.LoadName, GetNameKey(variable.Name));
                break;
        }
    }

    /// <summary>Initializes a binding with the value on the stack (no TDZ check, no immutability check).</summary>
    private void EmitInitializeVariable(Variable variable)
    {
        switch (variable.Location)
        {
            case VariableLocation.Register:
                _builder.Emit(OpCode.StoreLocal, variable.Index);
                break;
            case VariableLocation.Context:
                _builder.Emit(OpCode.StoreContext, GetContextDepth(variable.Scope), variable.Index);
                break;
            case VariableLocation.GlobalLexical:
                _builder.Emit(OpCode.InitializeGlobalLexical, GetNameKey(variable.Name));
                break;
            case VariableLocation.GlobalObject:
                _builder.Emit(OpCode.StoreGlobal, GetNameKey(variable.Name), IsStrict ? 1 : 0);
                break;
            default:
                _builder.Emit(OpCode.InitializeName, GetNameKey(variable.Name));
                break;
        }
    }

    /// <summary>Loads the value of an identifier reference.</summary>
    private void EmitLoadIdentifier(Identifier identifier, bool forTypeOf = false)
    {
        var reference = _analysis.References[identifier];
        if (reference.IsDynamic)
        {
            _builder.Emit(forTypeOf ? OpCode.LoadNameOrUndefined : OpCode.LoadName, GetNameKey(reference.Name));
            return;
        }

        if (reference.Variable is null)
        {
            _builder.Emit(forTypeOf ? OpCode.LoadGlobalOrUndefined : OpCode.LoadGlobal, GetNameKey(reference.Name));
            return;
        }

        EmitLoadVariable(reference.Variable, reference.NeedsTdzCheck);
    }

    /// <summary>Stores the value on the stack in an identifier reference (PutValue); the value is consumed.</summary>
    private void EmitStoreIdentifier(Identifier identifier)
    {
        var reference = _analysis.References[identifier];
        if (reference.IsDynamic)
        {
            _builder.Emit(OpCode.StoreName, GetNameKey(reference.Name), IsStrict ? 1 : 0);
            return;
        }

        var variable = reference.Variable;
        if (variable is null)
        {
            _builder.Emit(OpCode.StoreGlobal, GetNameKey(reference.Name), IsStrict ? 1 : 0);
            return;
        }

        if (variable.Kind == VariableKind.FunctionName)
        {
            // The name of a function expression is immutable: ignored in sloppy code, a TypeError in strict code
            _builder.Emit(OpCode.Pop);
            if (IsStrict)
            {
                EmitThrowConstAssign(variable, checkTdz: false);
            }

            return;
        }

        if (variable.IsImmutable)
        {
            _builder.Emit(OpCode.Pop);
            EmitThrowConstAssign(variable, reference.NeedsTdzCheck);
            return;
        }

        switch (variable.Location)
        {
            case VariableLocation.Register:
                if (reference.NeedsTdzCheck)
                {
                    _builder.Emit(OpCode.StoreLocalChecked, variable.Index, GetNameKey(variable.Name));
                }
                else
                {
                    _builder.Emit(OpCode.StoreLocal, variable.Index);
                }

                break;
            case VariableLocation.Context:
                if (reference.NeedsTdzCheck)
                {
                    _builder.Emit(OpCode.StoreContextChecked, GetContextDepth(variable.Scope), variable.Index, GetNameKey(variable.Name));
                }
                else
                {
                    _builder.Emit(OpCode.StoreContext, GetContextDepth(variable.Scope), variable.Index);
                }

                break;
            case VariableLocation.GlobalObject:
            case VariableLocation.GlobalLexical:
                _builder.Emit(OpCode.StoreGlobal, GetNameKey(variable.Name), IsStrict ? 1 : 0);
                break;
            default:
                _builder.Emit(OpCode.StoreName, GetNameKey(variable.Name), IsStrict ? 1 : 0);
                break;
        }
    }

    private void EmitThrowConstAssign(Variable variable, bool checkTdz)
    {
        switch (variable.Location)
        {
            case VariableLocation.Register:
                _builder.Emit(OpCode.ThrowConstAssignLocal, checkTdz ? variable.Index : -1, GetNameKey(variable.Name));
                break;
            case VariableLocation.Context:
                _builder.Emit(OpCode.ThrowConstAssignContext, GetContextDepth(variable.Scope), checkTdz ? variable.Index : -1, GetNameKey(variable.Name));
                break;
            default:
                _builder.Emit(OpCode.ThrowError, (int)ErrorType.TypeError, _builder.AddConstant("Assignment to constant variable."));
                break;
        }
    }

    /// <summary>Initializes the binding of an identifier of a declaration (let, const, class, parameters...).</summary>
    private void EmitInitializeIdentifier(Identifier identifier)
    {
        var reference = _analysis.References[identifier];
        if (reference.Variable is { } variable && !reference.IsDynamic)
        {
            if (variable.Kind is VariableKind.Var or VariableKind.Function or VariableKind.EvalVar)
            {
                // A var declaration is an assignment (PutValue)
                EmitStoreIdentifier(identifier);
            }
            else
            {
                EmitInitializeVariable(variable);
            }

            return;
        }

        EmitStoreIdentifier(identifier);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // this, new.target, super
    // ------------------------------------------------------------------------------------------------------------------

    private void EmitLoadSpecial(Node node, VariableKind kind, bool checkInitialized)
    {
        var reference = _analysis.GetSpecialReference(node, kind);
        switch (reference.Kind)
        {
            case SpecialReferenceKind.Frame:
                _builder.Emit(kind switch
                {
                    VariableKind.This => OpCode.LoadThis,
                    VariableKind.NewTarget => OpCode.LoadNewTarget,
                    VariableKind.HomeObject => OpCode.LoadHomeObject,
                    _ => OpCode.LoadFunction,
                });
                break;
            case SpecialReferenceKind.Variable:
                EmitLoadVariable(reference.Variable!, checkTdz: checkInitialized && kind == VariableKind.This);
                break;
            case SpecialReferenceKind.GlobalThis:
                _builder.Emit(OpCode.LoadGlobalThis);
                break;
            case SpecialReferenceKind.Undefined:
                _builder.Emit(OpCode.PushUndefined);
                break;
            default:
                _builder.Emit(OpCode.LoadName, GetNameKey(reference.Name));
                break;
        }
    }

    private void EmitLoadThis(Node node, bool checkInitialized)
    {
        if (_analysis.SpecialReferences.ContainsKey((node, VariableKind.This)))
        {
            EmitLoadSpecial(node, VariableKind.This, checkInitialized);
            return;
        }

        // The this of the function itself (the implicit return of a derived constructor)
        if (_function.ThisVariable is { } thisVariable)
        {
            EmitLoadVariable(thisVariable, checkTdz: checkInitialized);
        }
        else
        {
            _builder.Emit(OpCode.LoadThis);
        }
    }
}
