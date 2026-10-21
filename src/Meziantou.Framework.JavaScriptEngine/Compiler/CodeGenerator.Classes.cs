using Meziantou.Framework.JavaScriptEngine.Ast;

namespace Meziantou.Framework.JavaScriptEngine.Compiler;

internal sealed partial class CodeGenerator
{
    /// <summary>ClassDefinitionEvaluation: -> constructor (https://tc39.es/ecma262/#sec-runtime-semantics-classdefinitionevaluation).</summary>
    private void EmitClass(ClassNode node, string name)
    {
        _builder.SetPosition(node.Start);
        var outerScope = _scope;
        var classScope = _analysis.Scopes[node];
        EnterScope(classScope);

        // Each evaluation of the class creates new private names
        foreach (var variable in classScope.OrderedVariables)
        {
            if (variable.Kind == VariableKind.PrivateName)
            {
                _builder.Emit(OpCode.CreatePrivateName, _builder.AddConstant(variable.Name));
                EmitInitializeVariable(variable);
            }
        }

        if (node.SuperClass is not null)
        {
            EmitExpression(node.SuperClass);
        }
        else
        {
            _builder.Emit(OpCode.PushHole);
        }

        // The constructor (or the default constructor)
        var constructorMethod = node.Body.OfType<MethodDefinition>().FirstOrDefault(m => m.Kind == MethodKind.Constructor);
        FunctionTemplate constructorTemplate;
        if (constructorMethod is not null)
        {
            var info = _analysis.Functions[constructorMethod.Value];
            var generator = new CodeGenerator(_analysis, _source, info);
            constructorTemplate = generator.CompileFunctionBody(constructorMethod.Value, name, FunctionTemplateFlags.None);
        }
        else
        {
            constructorTemplate = CreateDefaultConstructor(name, node.SuperClass is not null, node);
        }

        var classTemplate = new ClassTemplate(name, constructorTemplate, node.SuperClass is not null, _source, node.Start, node.End);
        _builder.SetPosition(node.Start);
        _builder.Emit(OpCode.CreateClass, _builder.AddObject(classTemplate));

        var prototype = _function.AllocateRegister();
        var constructor = _function.AllocateRegister();
        _builder.Emit(OpCode.StoreLocal, prototype);
        _builder.Emit(OpCode.StoreLocal, constructor);

        foreach (var element in node.Body)
        {
            switch (element)
            {
                case MethodDefinition { Kind: MethodKind.Constructor }:
                    break;
                case MethodDefinition method:
                    EmitClassMethod(method, method.Static ? constructor : prototype, constructor);
                    break;
                case PropertyDefinition { Computed: true } field:
                    // The computed keys of the fields are evaluated now, in order
                    EmitExpression(field.Key);
                    _builder.Emit(OpCode.ToPropertyKey);
                    EmitInitializeVariable(_analysis.GetComputedFieldKey(field));
                    break;
            }
        }

        // The inner binding of the class name is initialized before the static elements run
        if (node.Id is not null && classScope.Find(node.Id.Name) is { } nameVariable)
        {
            _builder.Emit(OpCode.LoadLocal, constructor);
            EmitInitializeVariable(nameVariable);
        }

        // The instance fields: an initializer function called by InitializeInstanceElements
        if (_analysis.GetClassInitializer(node, isStatic: false) is { } instanceInitializer)
        {
            _builder.Emit(OpCode.LoadLocal, constructor);
            _builder.Emit(OpCode.CreateClosure, _builder.AddObject(CompileClassInitializer(node, instanceInitializer, isStatic: false)));
            _builder.Emit(OpCode.SetFieldInitializer);
        }

        // The static fields and blocks, in order, with the class as this
        if (_analysis.GetClassInitializer(node, isStatic: true) is { } staticInitializer)
        {
            _builder.Emit(OpCode.LoadLocal, constructor);
            _builder.Emit(OpCode.CreateClosure, _builder.AddObject(CompileClassInitializer(node, staticInitializer, isStatic: true)));
            _builder.Emit(OpCode.CallClassStaticInitializer);
        }

        _builder.Emit(OpCode.LoadLocal, constructor);
        ExitScope(classScope);
        _scope = outerScope;
    }

    /// <summary>
    /// The default constructor of a class: <c>constructor() {}</c>, or <c>constructor(...args) { super(...args); }</c> for a
    /// derived class (the arguments are forwarded without iterating them).
    /// </summary>
    private FunctionTemplate CreateDefaultConstructor(string name, bool isDerived, ClassNode node)
    {
        var builder = new BytecodeBuilder();
        var flags = FunctionTemplateFlags.Strict | FunctionTemplateFlags.ClassConstructor | FunctionTemplateFlags.Constructor | FunctionTemplateFlags.Method | FunctionTemplateFlags.DefaultConstructor;
        if (isDerived)
        {
            flags |= FunctionTemplateFlags.DerivedConstructor;
            builder.Emit(OpCode.LoadFunction);
            builder.Emit(OpCode.LoadNewTarget);
            builder.Emit(OpCode.LoadAllArguments);
            builder.Emit(OpCode.SuperCallSpread);
            builder.Emit(OpCode.BindThis);
            builder.Emit(OpCode.LoadFunction);
            builder.Emit(OpCode.InitializeInstanceElements);
        }
        else
        {
            builder.Emit(OpCode.LoadThis);
            builder.Emit(OpCode.LoadFunction);
            builder.Emit(OpCode.InitializeInstanceElements);
        }

        builder.Emit(OpCode.Return);
        var (code, constants, keys, objects, handlers, positions) = builder.Build();
        return new FunctionTemplate
        {
            Name = name,
            Length = 0,
            Flags = flags,
            Code = code,
            Constants = constants,
            Keys = keys,
            Objects = objects,
            RegisterCount = 0,
            MaxStack = builder.MaxStack + 1,
            Handlers = handlers,
            Positions = positions,
            Source = _source,
            SourceStart = node.Start,
            SourceEnd = node.End,
        };
    }

    /// <summary>Defines a method, a getter or a setter of a class (public or private).</summary>
    private void EmitClassMethod(MethodDefinition method, int target, int constructor)
    {
        var kind = method.Kind switch
        {
            MethodKind.Get => 1,
            MethodKind.Set => 2,
            _ => 0,
        };

        var prefix = kind switch
        {
            1 => "get ",
            2 => "set ",
            _ => "",
        };

        if (method.Key is PrivateIdentifier privateName)
        {
            _builder.Emit(OpCode.LoadLocal, constructor);
            _builder.Emit(OpCode.LoadLocal, target);
            _builder.Emit(OpCode.CreateClosure, CompileNestedFunction(method.Value, prefix + "#" + privateName.Name));
            EmitLoadVariable(FindVariable("#" + privateName.Name), checkTdz: false);
            _builder.Emit(OpCode.DefinePrivateMethod, kind, method.Static ? 1 : 0);
            return;
        }

        _builder.Emit(OpCode.LoadLocal, target);
        if (method.Computed)
        {
            EmitExpression(method.Key);
            _builder.Emit(OpCode.ToPropertyKey);
            _builder.Emit(OpCode.CreateClosure, CompileNestedFunction(method.Value, ""));
            _builder.Emit(OpCode.Swap);
            _builder.Emit(OpCode.Dup);
            _builder.Emit(OpCode.Rot3);
            _builder.Emit(OpCode.SetFunctionNameComputed, kind);
        }
        else
        {
            var key = GetPropertyKey(method.Key);
            EmitPushKey(key);
            _builder.Emit(OpCode.CreateClosure, CompileNestedFunction(method.Value, prefix + GetFunctionNameForKey(key)));
        }

        _builder.Emit(OpCode.DefineMethod, kind, 0);
        _builder.Emit(OpCode.Pop);
    }

    /// <summary>Compiles the synthetic function that defines the fields (and runs the static blocks) of a class with this as the target.</summary>
    private FunctionTemplate CompileClassInitializer(ClassNode node, FunctionInfo info, bool isStatic)
    {
        var generator = new CodeGenerator(_analysis, _source, info);
        return generator.CompileClassInitializerBody(node, isStatic);
    }

    private FunctionTemplate CompileClassInitializerBody(ClassNode node, bool isStatic)
    {
        var scope = _function.Scope;
        _scope = scope;
        if (scope.NeedsContext)
        {
            _builder.Emit(OpCode.PushScope, _builder.AddObject(scope.Info!));
        }

        EmitSpecialBindings();
        EnterScope(_function.LexicalScope!);
        foreach (var element in node.Body)
        {
            switch (element)
            {
                case PropertyDefinition field when field.Static == isStatic:
                    EmitFieldDefinition(field);
                    break;
                case StaticBlock block when isStatic:
                    // A static block is a method called with the class as this
                    _builder.Emit(OpCode.CreateClosure, _builder.AddObject(CompileStaticBlock(block)));
                    _builder.Emit(OpCode.LoadHomeObject);
                    _builder.Emit(OpCode.SetHomeObject);
                    _builder.Emit(OpCode.LoadThis);
                    _builder.Emit(OpCode.Call, 0);
                    _builder.Emit(OpCode.Pop);
                    break;
            }
        }

        _builder.Emit(OpCode.PushUndefined);
        _builder.Emit(OpCode.Return);
        var flags = FunctionTemplateFlags.Strict | FunctionTemplateFlags.Method | FunctionTemplateFlags.ClassFieldInitializer;
        return Build(string.Empty, length: 0, flags, node.Start, node.End, nameScope: null);
    }

    private FunctionTemplate CompileStaticBlock(StaticBlock block)
    {
        var info = _analysis.Functions[block];
        var generator = new CodeGenerator(_analysis, _source, info);
        return generator.CompileStaticBlockBody(block);
    }

    private FunctionTemplate CompileStaticBlockBody(StaticBlock block)
    {
        var scope = _function.Scope;
        _scope = scope;
        if (scope.NeedsContext)
        {
            _builder.Emit(OpCode.PushScope, _builder.AddObject(scope.Info!));
        }

        EmitSpecialBindings();
        EmitVarInitialization(scope, parameterScope: null);
        EnterScope(_function.LexicalScope!);
        EmitFunctionDeclarations(block.Body, scope, isGlobal: false);
        EmitStatements(block.Body);
        if (!_builder.IsUnreachable)
        {
            _builder.Emit(OpCode.PushUndefined);
            _builder.Emit(OpCode.Return);
        }

        var flags = FunctionTemplateFlags.Strict | FunctionTemplateFlags.Method;
        return Build(string.Empty, length: 0, flags, block.Start, block.End, nameScope: null);
    }

    /// <summary>DefineField: this key value (the anonymous functions are named after the key).</summary>
    private void EmitFieldDefinition(PropertyDefinition field)
    {
        _builder.SetPosition(field.Start);
        _builder.Emit(OpCode.LoadThis);
        if (field.Key is PrivateIdentifier privateName)
        {
            EmitLoadVariable(FindVariable("#" + privateName.Name), checkTdz: false);
            EmitFieldValue(field, "#" + privateName.Name);
            _builder.Emit(OpCode.DefinePrivateField);
            return;
        }

        if (field.Computed)
        {
            EmitLoadVariable(_analysis.GetComputedFieldKey(field), checkTdz: false);
            if (field.Value is not null && IsAnonymousFunctionDefinition(field.Value))
            {
                EmitNamedEvaluationWithKeyOnStack(field.Value, prefix: 0);
            }
            else
            {
                EmitFieldValue(field, name: null);
            }

            _builder.Emit(OpCode.DefineClassFieldComputed);
            return;
        }

        var key = GetPropertyKey(field.Key);
        EmitFieldValue(field, GetFunctionNameForKey(key));
        _builder.Emit(OpCode.DefineClassField, _builder.AddKey(key));
    }

    private void EmitFieldValue(PropertyDefinition field, string? name)
    {
        if (field.Value is null)
        {
            _builder.Emit(OpCode.PushUndefined);
        }
        else if (name is not null)
        {
            EmitNamedEvaluation(field.Value, name);
        }
        else
        {
            EmitExpression(field.Value);
        }
    }
}
