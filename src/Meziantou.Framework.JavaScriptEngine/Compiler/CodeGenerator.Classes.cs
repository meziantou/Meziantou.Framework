using Meziantou.Framework.JavaScriptEngine.Ast;

namespace Meziantou.Framework.JavaScriptEngine.Compiler;

internal sealed partial class CodeGenerator
{
    /// <summary>ClassDefinitionEvaluation: -> constructor (https://tc39.es/ecma262/#sec-runtime-semantics-classdefinitionevaluation).</summary>
    private void EmitClass(ClassNode node, string name)
    {
        _builder.SetPosition(node.Start);

        // The decorators of the class are evaluated first, in the enclosing scope (and in the strictness of the enclosing code)
        var classDecoration = -1;
        if (node.HasDecorators())
        {
            classDecoration = _function.AllocateRegister();
            _builder.Emit(OpCode.CreateClassDecoration);
            EmitDecorators(node.Decorators);
            _builder.Emit(OpCode.StoreLocal, classDecoration);
        }

        // All parts of a class are strict: the heritage and the computed keys run as strict code in sloppy code too
        if (!_function.IsStrict && _classStrictDepth++ == 0)
        {
            _builder.Emit(OpCode.EnterStrictCode);
        }

        EmitClassDefinition(node, name, classDecoration);
        if (!_function.IsStrict && --_classStrictDepth == 0)
        {
            _builder.Emit(OpCode.ExitStrictCode);
        }
    }

    /// <summary>The class definition; <paramref name="classDecoration"/> is the register of the decoration state of a decorated class, else -1.</summary>
    private void EmitClassDefinition(ClassNode node, string name, int classDecoration)
    {
        var isDecorated = classDecoration >= 0;
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
            constructorTemplate = CompileNestedFunctionTemplate(constructorMethod.Value, name, FunctionTemplateFlags.None);
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
                case MethodDefinition method when isDecorated:
                    EmitDecoratedClassMethod(method, method.Static ? constructor : prototype);
                    break;
                case MethodDefinition method:
                    EmitClassMethod(method, method.Static ? constructor : prototype, constructor);
                    break;
                case PropertyDefinition { IsAccessor: true } accessor:
                    EmitAutoAccessorDefinition(accessor, accessor.Static ? constructor : prototype, constructor, isDecorated);
                    break;
                case PropertyDefinition field when field.Decorators is not null:
                    EmitCreateElementDecoration(field, DecoratedElementKind.Field, field.Static ? constructor : prototype);
                    EmitLoadVariable(_analysis.GetElementDecoration(field), checkTdz: false);
                    EmitClassElementKey(field.Key, field.Computed, field.Computed ? _analysis.GetComputedFieldKey(field) : null);
                    _builder.Emit(OpCode.SetDecoratedKey);
                    break;
                case PropertyDefinition { Computed: true } field:
                    // The computed keys of the fields are evaluated now, in order
                    EmitExpression(field.Key);
                    _builder.Emit(OpCode.ToPropertyKey);
                    EmitInitializeVariable(_analysis.GetComputedFieldKey(field));
                    break;
            }
        }

        var instanceInitializer = _analysis.GetClassInitializer(node, isStatic: false);
        var staticInitializer = _analysis.GetClassInitializer(node, isStatic: true);
        if (isDecorated)
        {
            EmitDecoratedClassEnd(node, name, classDecoration, constructor, classScope, instanceInitializer, staticInitializer);
            ExitScope(classScope);
            _scope = outerScope;
            return;
        }

        // The inner binding of the class name is initialized before the static elements run
        if (node.Id is not null && classScope.Find(node.Id.Name) is { } nameVariable)
        {
            _builder.Emit(OpCode.LoadLocal, constructor);
            EmitInitializeVariable(nameVariable);
        }

        // The instance fields: an initializer function called by InitializeInstanceElements
        if (instanceInitializer is not null)
        {
            _builder.Emit(OpCode.LoadLocal, constructor);
            _builder.Emit(OpCode.CreateClosure, _builder.AddObject(CompileClassInitializer(node, instanceInitializer, isStatic: false)));
            _builder.Emit(OpCode.SetFieldInitializer);
        }

        // The static fields and blocks, in order, with the class as this
        if (staticInitializer is not null)
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
    /// The end of the definition of a decorated class: the decorators of the elements are applied (the elements that are
    /// not fields are defined then), then the decorators of the class; the static elements run with the decorated class as
    /// this. The decorated class is on the stack.
    /// </summary>
    private void EmitDecoratedClassEnd(ClassNode node, string name, int classDecoration, int constructor, Scope classScope, FunctionInfo? instanceInitializer, FunctionInfo? staticInitializer)
    {
        _builder.SetPosition(node.Start);
        _builder.Emit(OpCode.LoadLocal, classDecoration);
        _builder.Emit(OpCode.LoadLocal, constructor);
        _builder.Emit(OpCode.BeginClassDecoration);

        // The static elements that are not fields, the instance ones, then the static fields and the instance fields
        EmitApplyElementDecorators(node, classDecoration, isStatic: true, fields: false);
        EmitApplyElementDecorators(node, classDecoration, isStatic: false, fields: false);
        EmitApplyElementDecorators(node, classDecoration, isStatic: true, fields: true);
        EmitApplyElementDecorators(node, classDecoration, isStatic: false, fields: true);

        // The instance elements are set before the class decorators run: they can create instances
        if (instanceInitializer is not null)
        {
            _builder.Emit(OpCode.LoadLocal, constructor);
            _builder.Emit(OpCode.CreateClosure, _builder.AddObject(CompileClassInitializer(node, instanceInitializer, isStatic: false)));
            _builder.Emit(OpCode.SetFieldInitializer);
        }

        _builder.SetPosition(node.Start);
        _builder.Emit(OpCode.LoadLocal, classDecoration);
        _builder.Emit(OpCode.ApplyClassDecorators, _builder.AddConstant(name));
        var result = _function.AllocateRegister();
        _builder.Emit(OpCode.StoreLocal, result);

        // The inner binding of the class name is the decorated class
        if (node.Id is not null && classScope.Find(node.Id.Name) is { } nameVariable)
        {
            _builder.Emit(OpCode.LoadLocal, result);
            EmitInitializeVariable(nameVariable);
        }

        _builder.Emit(OpCode.LoadLocal, classDecoration);
        _builder.Emit(OpCode.RunClassDecorationInitializers, 0);

        // The static fields and blocks, in order, with the decorated class as this (their home object is the class)
        if (staticInitializer is not null)
        {
            _builder.Emit(OpCode.CreateClosure, _builder.AddObject(CompileClassInitializer(node, staticInitializer, isStatic: true)));
            _builder.Emit(OpCode.LoadLocal, constructor);
            _builder.Emit(OpCode.SetHomeObject);
            _builder.Emit(OpCode.LoadLocal, result);
            _builder.Emit(OpCode.Call, 0);
            _builder.Emit(OpCode.Pop);
        }

        _builder.Emit(OpCode.LoadLocal, classDecoration);
        _builder.Emit(OpCode.RunClassDecorationInitializers, 1);
        _builder.Emit(OpCode.LoadLocal, result);
    }

    private void EmitApplyElementDecorators(ClassNode node, int classDecoration, bool isStatic, bool fields)
    {
        foreach (var element in node.Body)
        {
            var applies = element switch
            {
                MethodDefinition { Kind: not MethodKind.Constructor } method => !fields && method.Static == isStatic,
                PropertyDefinition { IsAccessor: true } accessor => !fields && accessor.Static == isStatic,
                PropertyDefinition { Decorators: not null } field => fields && field.Static == isStatic,
                _ => false,
            };

            if (applies)
            {
                _builder.SetPosition(element.Start);
                _builder.Emit(OpCode.LoadLocal, classDecoration);
                EmitLoadVariable(_analysis.GetElementDecoration(element), checkTdz: false);
                _builder.Emit(OpCode.ApplyElementDecorators);
            }
        }
    }

    /// <summary>Evaluates decorators (DecoratorListEvaluation): each one adds its value and its receiver to the decoration state on the stack.</summary>
    private void EmitDecorators(List<Node>? decorators)
    {
        if (decorators is null)
            return;

        foreach (var decorator in decorators)
        {
            _builder.SetPosition(decorator.Start);
            EmitCallee(decorator);
            _builder.Emit(OpCode.AddDecorator);
        }
    }

    /// <summary>Creates the decoration state of an element of a decorated class, with its decorators, in its hidden binding.</summary>
    private void EmitCreateElementDecoration(Node element, DecoratedElementKind kind, int home)
    {
        var (decorators, isStatic) = element switch
        {
            MethodDefinition method => (method.Decorators, method.Static),
            PropertyDefinition property => (property.Decorators, property.Static),
            _ => throw new InvalidOperationException("Unexpected class element: " + element.Type),
        };

        _builder.SetPosition(element.Start);
        _builder.Emit(OpCode.LoadLocal, home);
        _builder.Emit(OpCode.CreateElementDecoration, (int)kind, isStatic ? 1 : 0);
        EmitDecorators(decorators);
        EmitInitializeVariable(_analysis.GetElementDecoration(element));
    }

    /// <summary>Pushes the key of a class element: its private name, its computed key (also stored in <paramref name="keyVariable"/>) or its constant key.</summary>
    private void EmitClassElementKey(Node key, bool computed, Variable? keyVariable)
    {
        if (key is PrivateIdentifier privateName)
        {
            EmitLoadVariable(FindVariable("#" + privateName.Name), checkTdz: false);
        }
        else if (computed)
        {
            EmitExpression(key);
            _builder.Emit(OpCode.ToPropertyKey);
            if (keyVariable is not null)
            {
                _builder.Emit(OpCode.Dup);
                EmitInitializeVariable(keyVariable);
            }
        }
        else
        {
            EmitPushKey(GetPropertyKey(key));
        }
    }

    /// <summary>A method, a getter or a setter of a decorated class: it is defined when the decorators are applied.</summary>
    private void EmitDecoratedClassMethod(MethodDefinition method, int home)
    {
        var (kind, prefix) = method.Kind switch
        {
            MethodKind.Get => (DecoratedElementKind.Getter, "get "),
            MethodKind.Set => (DecoratedElementKind.Setter, "set "),
            _ => (DecoratedElementKind.Method, ""),
        };

        EmitCreateElementDecoration(method, kind, home);
        EmitLoadVariable(_analysis.GetElementDecoration(method), checkTdz: false);
        EmitClassElementKey(method.Key, method.Computed, keyVariable: null);
        var name = method.Key switch
        {
            PrivateIdentifier privateName => prefix + "#" + privateName.Name,
            _ when method.Computed => "",
            _ => prefix + GetFunctionNameForKey(GetPropertyKey(method.Key)),
        };

        _builder.Emit(OpCode.CreateClosure, CompileNestedFunction(method.Value, name));
        _builder.Emit(OpCode.SetDecoratedMethod, method.Computed ? 1 : 0);
    }

    /// <summary>
    /// An auto-accessor (<c>accessor key = value;</c>): a getter and a setter of a private storage, defined now (or when
    /// the decorators are applied in a decorated class). The private name of the storage is in a hidden binding.
    /// </summary>
    private void EmitAutoAccessorDefinition(PropertyDefinition accessor, int home, int constructor, bool isDecorated)
    {
        var keyVariable = accessor.Computed ? _analysis.GetComputedFieldKey(accessor) : null;
        if (isDecorated)
        {
            EmitCreateElementDecoration(accessor, DecoratedElementKind.Accessor, home);
            EmitLoadVariable(_analysis.GetElementDecoration(accessor), checkTdz: false);
            EmitClassElementKey(accessor.Key, accessor.Computed, keyVariable);
            _builder.Emit(OpCode.SetDecoratedAccessor);
        }
        else
        {
            _builder.SetPosition(accessor.Start);
            _builder.Emit(OpCode.LoadLocal, constructor);
            _builder.Emit(OpCode.LoadLocal, home);
            EmitClassElementKey(accessor.Key, accessor.Computed, keyVariable);
            _builder.Emit(OpCode.DefineAutoAccessor, accessor.Static ? 1 : 0);
        }

        EmitInitializeVariable(_analysis.GetAccessorStorage(accessor));
    }

    /// <summary>
    /// The default constructor of a class: <c>constructor() {}</c>, or <c>constructor(...args) { super(...args); }</c> for a
    /// derived class (the arguments are forwarded without iterating them).
    /// </summary>
    private FunctionTemplate CreateDefaultConstructor(string name, bool isDerived, ClassNode node)
    {
        var builder = BytecodeBuilder.Rent();
        var flags = FunctionTemplateFlags.Strict | FunctionTemplateFlags.ClassConstructor | FunctionTemplateFlags.Constructor | FunctionTemplateFlags.Method | FunctionTemplateFlags.DefaultConstructor;
        if (isDerived)
        {
            flags |= FunctionTemplateFlags.DerivedConstructor;
            builder.Emit(OpCode.LoadFunction);
            builder.Emit(OpCode.LoadSuperBase);
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
        var template = new FunctionTemplate
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
            Caches = new PropertyCacheEntry?[builder.CacheCount],
            Handlers = handlers,
            Positions = positions,
            Source = _source,
            SourceStart = node.Start,
            SourceEnd = node.End,
        };

        builder.Return();
        return template;
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
        EmitWithDisposal(_function.LexicalScope!, () => EmitStatements(block.Body));
        if (!_builder.IsUnreachable)
        {
            _builder.Emit(OpCode.PushUndefined);
            _builder.Emit(OpCode.Return);
        }

        var flags = FunctionTemplateFlags.Strict | FunctionTemplateFlags.Method;
        return Build(string.Empty, length: 0, flags, block.Start, block.End, nameScope: null);
    }

    /// <summary>
    /// InitializeFieldOrAccessor: DefineField this key value (the anonymous functions are named after the key), after the
    /// initializers added by the decorators; then the extra initializers of the decorators. An auto-accessor defines the
    /// private field of its storage.
    /// </summary>
    private void EmitFieldDefinition(PropertyDefinition field)
    {
        _builder.SetPosition(field.Start);
        _builder.Emit(OpCode.LoadThis);
        if (field.IsAccessor)
        {
            EmitLoadVariable(_analysis.GetAccessorStorage(field), checkTdz: false);
            if (field.Key is PrivateIdentifier privateKey)
            {
                EmitFieldValue(field, "#" + privateKey.Name);
            }
            else if (field.Computed)
            {
                if (field.Value is not null && IsAnonymousFunctionDefinition(field.Value))
                {
                    EmitLoadVariable(_analysis.GetComputedFieldKey(field), checkTdz: false);
                    EmitNamedEvaluationWithKeyOnStack(field.Value, prefix: 0);
                    _builder.Emit(OpCode.Swap);
                    _builder.Emit(OpCode.Pop);
                }
                else
                {
                    EmitFieldValue(field, name: null);
                }
            }
            else
            {
                EmitFieldValue(field, GetFunctionNameForKey(GetPropertyKey(field.Key)));
            }

            EmitFieldDecoratorInitializers(field);
            _builder.Emit(OpCode.DefinePrivateField);
            EmitFieldExtraInitializers(field);
            return;
        }

        if (field.Key is PrivateIdentifier privateName)
        {
            EmitLoadVariable(FindVariable("#" + privateName.Name), checkTdz: false);
            EmitFieldValue(field, "#" + privateName.Name);
            EmitFieldDecoratorInitializers(field);
            _builder.Emit(OpCode.DefinePrivateField);
            EmitFieldExtraInitializers(field);
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

            EmitFieldDecoratorInitializers(field);
            _builder.Emit(OpCode.DefineClassFieldComputed);
            EmitFieldExtraInitializers(field);
            return;
        }

        var key = GetPropertyKey(field.Key);
        EmitFieldValue(field, GetFunctionNameForKey(key));
        EmitFieldDecoratorInitializers(field);
        _builder.Emit(OpCode.DefineClassField, _builder.AddKey(key));
        EmitFieldExtraInitializers(field);
    }

    /// <summary>value -> value: the initializers that the decorators of the field added.</summary>
    private void EmitFieldDecoratorInitializers(PropertyDefinition field)
    {
        if (field.Decorators is null)
            return;

        _builder.Emit(OpCode.LoadThis);
        EmitLoadVariable(_analysis.GetElementDecoration(field), checkTdz: false);
        _builder.Emit(OpCode.RunFieldInitializers);
    }

    /// <summary>The initializers that the decorators of the field added with addInitializer, once the field is defined.</summary>
    private void EmitFieldExtraInitializers(PropertyDefinition field)
    {
        if (field.Decorators is null)
            return;

        _builder.Emit(OpCode.LoadThis);
        EmitLoadVariable(_analysis.GetElementDecoration(field), checkTdz: false);
        _builder.Emit(OpCode.RunExtraInitializers);
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
