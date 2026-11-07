using Meziantou.Framework.JavaScriptEngine.Compiler;

namespace Meziantou.Framework.JavaScriptEngine.Interpreter;

// Decorators (https://github.com/tc39/proposal-decorators, https://arai-a.github.io/ecma262-compare/?pr=2417) and decorator
// metadata (https://github.com/tc39/proposal-decorator-metadata).
//
// The class definition evaluates the decorators of the class (before the class scope), then each element: its decorators
// and its key, in order. In a class that has a decorator, the elements other than the fields (the methods, the getters,
// the setters and the auto-accessors) are defined after all the elements are evaluated, with their decorators applied:
// first the static elements, then the instance elements. The decorators of the fields are applied after them (static,
// then instance), then the decorators of the class. The state of the decoration of an element is in a hidden binding of
// the class scope: the field initializers read the initializers that the decorators added.
internal static partial class JsInterpreter
{
    /// <summary>CreateClassDecoration: -> classDecoration</summary>
    private static int ExecuteCreateClassDecoration(Frame frame, int sp)
    {
        frame.Values[sp++] = new JsValue(new ClassDecorationCell());
        frame.Pc++;
        return sp;
    }

    /// <summary>AddDecorator: decoration function thisValue -> decoration (DecoratorEvaluation: the decorator and its receiver)</summary>
    private static int ExecuteAddDecorator(Frame frame, int sp)
    {
        var values = frame.Values;
        var receiver = values[--sp];
        var decorator = values[--sp];
        var decoration = (DecorationCell)values[sp - 1].Cell!;
        decoration.Decorators.Add(new DecoratorDefinition(decorator, receiver));
        frame.Pc++;
        return sp;
    }

    /// <summary>CreateElementDecoration (kind, static): homeObject -> decoration</summary>
    private static int ExecuteCreateElementDecoration(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        var kind = (DecoratedElementKind)code[pc++];
        var isStatic = code[pc++] != 0;
        var home = (JsObject)values[sp - 1].Cell!;
        values[sp - 1] = new JsValue(new ElementDecorationCell(kind, isStatic, home));
        frame.Pc = pc;
        return sp;
    }

    /// <summary>SetDecoratedMethod (nameFromKey): decoration key function ->: the method, getter or setter of the element (MethodDefinitionEvaluation)</summary>
    private static int ExecuteSetDecoratedMethod(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        var nameFromKey = code[pc++] != 0;
        var function = (JsClosure)values[--sp].Cell!;
        var key = values[--sp];
        var decoration = (ElementDecorationCell)values[--sp].Cell!;
        decoration.SetKey(key);
        function.HomeObject = decoration.Home;
        if (nameFromKey)
        {
            function.SetFunctionName(decoration.GetFunctionName(), decoration.ElementKind switch
            {
                DecoratedElementKind.Getter => "get",
                DecoratedElementKind.Setter => "set",
                _ => null,
            });
        }

        switch (decoration.ElementKind)
        {
            case DecoratedElementKind.Getter:
                decoration.Getter = function;
                break;
            case DecoratedElementKind.Setter:
                decoration.Setter = function;
                break;
            default:
                decoration.Value = function;
                break;
        }

        frame.Pc = pc;
        return sp;
    }

    /// <summary>SetDecoratedKey: decoration key ->: the key of a decorated field</summary>
    private static int ExecuteSetDecoratedKey(Frame frame, int sp)
    {
        var values = frame.Values;
        var key = values[--sp];
        ((ElementDecorationCell)values[--sp].Cell!).SetKey(key);
        frame.Pc++;
        return sp;
    }

    /// <summary>SetDecoratedAccessor: decoration key -> storage: the getter and the setter of an auto-accessor of a decorated class (defined later)</summary>
    private static int ExecuteSetDecoratedAccessor(Frame frame, int sp)
    {
        var values = frame.Values;
        var key = values[--sp];
        var decoration = (ElementDecorationCell)values[sp - 1].Cell!;
        decoration.SetKey(key);
        var storage = CreateAutoAccessorStorage(decoration.PrivateKey, decoration.Key);
        (decoration.Getter, decoration.Setter) = CreateAutoAccessor(frame.Realm, decoration.GetFunctionName(), storage);
        values[sp - 1] = new JsValue(new PrivateNameCell(storage));
        frame.Pc++;
        return sp;
    }

    /// <summary>DefineAutoAccessor (static): constructor homeObject key -> storage: defines the getter and the setter of an auto-accessor (ClassFieldDefinitionEvaluation)</summary>
    private static int ExecuteDefineAutoAccessor(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        var isStatic = code[pc++] != 0;
        var keyValue = values[--sp];
        var home = (JsObject)values[--sp].Cell!;
        var constructor = (JsClosure)values[sp - 1].Cell!;
        var privateKey = (keyValue.Cell as PrivateNameCell)?.Name;
        var key = privateKey is null ? JsOperations.ToPropertyKey(keyValue) : default;
        var storage = CreateAutoAccessorStorage(privateKey, key);
        var (getter, setter) = CreateAutoAccessor(frame.Realm, privateKey is null ? key : new PropertyKey(privateKey.ToString()), storage);
        if (privateKey is null)
        {
            DefineMethodProperty(home, key, DecoratedElementKind.Accessor, default, getter, setter);
        }
        else
        {
            DefinePrivateMethod(constructor, getter, privateKey, kind: 1, isStatic);
            DefinePrivateMethod(constructor, setter, privateKey, kind: 2, isStatic);
        }

        values[sp - 1] = new JsValue(new PrivateNameCell(storage));
        frame.Pc = pc;
        return sp;
    }

    /// <summary>BeginClassDecoration: classDecoration constructor ->: the metadata object, created once the elements are evaluated</summary>
    private static int ExecuteBeginClassDecoration(Frame frame, int sp)
    {
        var values = frame.Values;
        var constructor = (JsClosure)values[--sp].Cell!;
        var decoration = (ClassDecorationCell)values[--sp].Cell!;
        decoration.Constructor = constructor;
        frame.Sp = sp;

        // The metadata of the class inherits from the metadata of the superclass (the [[Prototype]] of the constructor)
        JsObject? metadataParent = null;
        if (constructor.IsDerivedConstructor && constructor.GetPrototypeOf() is { } constructorParent)
        {
            metadataParent = constructorParent.Get(new PropertyKey(WellKnownSymbols.Metadata)).Cell as JsObject;
        }

        decoration.Metadata = new JsObject(metadataParent);
        frame.Pc++;
        return sp;
    }

    /// <summary>ApplyElementDecorators: classDecoration decoration ->: ApplyDecoratorsToElementDefinition, then DefineMethodProperty for the elements that are not fields</summary>
    private static int ExecuteApplyElementDecorators(Frame frame, int sp)
    {
        var values = frame.Values;
        var element = (ElementDecorationCell)values[--sp].Cell!;
        var decoration = (ClassDecorationCell)values[--sp].Cell!;
        frame.Sp = sp;
        ApplyElementDecorators(frame.Realm, decoration, element);
        frame.Pc++;
        return sp;
    }

    /// <summary>ApplyClassDecorators (name): classDecoration -> class: ApplyDecoratorsToClassDefinition, then defines the metadata of the class</summary>
    private static int ExecuteApplyClassDecorators(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        var name = frame.Code.Constants[code[pc++]];
        var decoration = (ClassDecorationCell)values[sp - 1].Cell!;
        frame.Sp = sp;
        var constructor = decoration.Constructor!;
        if (decoration.InstanceMethodInitializers.Count > 0)
        {
            constructor.Initializers = decoration.InstanceMethodInitializers;
        }

        var realm = frame.Realm;
        JsValue classDefinition = constructor;
        var decorators = decoration.Decorators;
        for (var i = decorators.Count - 1; i >= 0; i--)
        {
            var state = new DecorationState();
            var context = CreateDecoratorContext(realm, kind: null, name, decoration.ClassInitializers, state, decoration.Metadata!, isStatic: false);
            var newDefinition = JsOperations.Call(decorators[i].Decorator, decorators[i].Receiver, [classDefinition, context]);
            state.Finished = true;
            if (newDefinition.IsCallable)
            {
                classDefinition = newDefinition;
            }
            else if (!newDefinition.IsUndefined)
            {
                ThrowHelper.ThrowTypeError("A class decorator must return a function or undefined");
            }
        }

        var result = classDefinition.AsObject();
        result.DefinePropertyOrThrow(new PropertyKey(WellKnownSymbols.Metadata), PropertyDescriptor.Data(decoration.Metadata, PropertyFlags.Writable | PropertyFlags.Configurable));
        decoration.Result = result;
        values[sp - 1] = classDefinition;
        frame.Pc = pc;
        return sp;
    }

    /// <summary>RunClassDecorationInitializers (class): classDecoration ->: the initializers of the static methods (0) or of the class (1), with the class as this</summary>
    private static int ExecuteRunClassDecorationInitializers(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        var ofClass = code[pc++] != 0;
        var decoration = (ClassDecorationCell)values[--sp].Cell!;
        frame.Sp = sp;
        var initializers = ofClass ? decoration.ClassInitializers : decoration.StaticMethodInitializers;
        JsValue receiver = decoration.Result;
        foreach (var initializer in initializers)
        {
            initializer.AsObject().Call(receiver, []);
        }

        frame.Pc = pc;
        return sp;
    }

    /// <summary>RunFieldInitializers: value receiver decoration -> value: the initializers that the decorators of a field or an auto-accessor added</summary>
    private static int ExecuteRunFieldInitializers(Frame frame, int sp)
    {
        var values = frame.Values;
        var decoration = (ElementDecorationCell)values[--sp].Cell!;
        var receiver = values[--sp];
        frame.Sp = sp;
        if (decoration.Initializers is { } initializers)
        {
            var value = values[sp - 1];
            foreach (var initializer in initializers)
            {
                value = initializer.AsObject().Call(receiver, [value]);
            }

            values[sp - 1] = value;
        }

        frame.Pc++;
        return sp;
    }

    /// <summary>RunExtraInitializers: receiver decoration ->: the initializers that the decorators of a field or an auto-accessor added with addInitializer</summary>
    private static int ExecuteRunExtraInitializers(Frame frame, int sp)
    {
        var values = frame.Values;
        var decoration = (ElementDecorationCell)values[--sp].Cell!;
        var receiver = values[--sp];
        frame.Sp = sp;
        if (decoration.ExtraInitializers is { } initializers)
        {
            foreach (var initializer in initializers)
            {
                initializer.AsObject().Call(receiver, []);
            }
        }

        frame.Pc++;
        return sp;
    }

    /// <summary>ApplyDecoratorsToElementDefinition, then DefineMethodProperty (or the private methods) for the elements that are not fields.</summary>
    private static void ApplyElementDecorators(JsRealm realm, ClassDecorationCell classDecoration, ElementDecorationCell element)
    {
        var kind = element.ElementKind;
        List<JsValue> extraInitializers;
        if (kind is DecoratedElementKind.Field or DecoratedElementKind.Accessor)
        {
            extraInitializers = element.ExtraInitializers ??= [];
        }
        else
        {
            extraInitializers = element.IsStatic ? classDecoration.StaticMethodInitializers : classDecoration.InstanceMethodInitializers;
        }

        var name = element.PrivateKey is { } privateName ? new JsValue(new PrivateNameCell(privateName)) : element.Key.ToJsValue();
        var decorators = element.Decorators;
        for (var i = decorators.Count - 1; i >= 0; i--)
        {
            var state = new DecorationState();
            var context = CreateDecoratorContext(realm, kind, name, extraInitializers, state, classDecoration.Metadata!, element.IsStatic);
            JsValue value;
            switch (kind)
            {
                case DecoratedElementKind.Method:
                    value = element.Value;
                    break;
                case DecoratedElementKind.Getter:
                    value = element.Getter;
                    break;
                case DecoratedElementKind.Setter:
                    value = element.Setter;
                    break;
                case DecoratedElementKind.Accessor:
                    var accessor = new JsObject(realm.Intrinsics.ObjectPrototype);
                    accessor.CreateDataPropertyOrThrow(new PropertyKey("get"), element.Getter);
                    accessor.CreateDataPropertyOrThrow(new PropertyKey("set"), element.Setter);
                    value = accessor;
                    break;
                default:
                    value = default;
                    break;
            }

            var newValue = JsOperations.Call(decorators[i].Decorator, decorators[i].Receiver, [value, context]);
            state.Finished = true;
            switch (kind)
            {
                case DecoratedElementKind.Field:
                    if (newValue.IsCallable)
                    {
                        (element.Initializers ??= []).Insert(0, newValue);
                    }
                    else if (!newValue.IsUndefined)
                    {
                        ThrowHelper.ThrowTypeError("A field decorator must return a function or undefined");
                    }

                    break;
                case DecoratedElementKind.Accessor:
                    if (newValue.Cell is JsObject result)
                    {
                        if (GetDecoratorResultFunction(result, "get") is { } newGetter)
                        {
                            element.Getter = newGetter;
                        }

                        if (GetDecoratorResultFunction(result, "set") is { } newSetter)
                        {
                            element.Setter = newSetter;
                        }

                        if (GetDecoratorResultFunction(result, "init") is { } initializer)
                        {
                            (element.Initializers ??= []).Insert(0, initializer);
                        }
                    }
                    else if (!newValue.IsUndefined)
                    {
                        ThrowHelper.ThrowTypeError("An accessor decorator must return an object or undefined");
                    }

                    break;
                default:
                    if (newValue.IsCallable)
                    {
                        switch (kind)
                        {
                            case DecoratedElementKind.Getter:
                                element.Getter = newValue;
                                break;
                            case DecoratedElementKind.Setter:
                                element.Setter = newValue;
                                break;
                            default:
                                element.Value = newValue;
                                break;
                        }
                    }
                    else if (!newValue.IsUndefined)
                    {
                        ThrowHelper.ThrowTypeError("A method decorator must return a function or undefined");
                    }

                    break;
            }
        }

        if (kind == DecoratedElementKind.Field)
            return;

        if (element.PrivateKey is not { } privateKey)
        {
            DefineMethodProperty(element.Home, element.Key, kind, element.Value, element.Getter, element.Setter);
            return;
        }

        // InitializePrivateMethods: the static ones are added to the class, the instance ones to [[PrivateMethods]]
        var constructor = classDecoration.Constructor!;
        switch (kind)
        {
            case DecoratedElementKind.Method:
                DefinePrivateMethod(constructor, element.Value, privateKey, kind: 0, element.IsStatic);
                break;
            case DecoratedElementKind.Getter:
                DefinePrivateMethod(constructor, element.Getter, privateKey, kind: 1, element.IsStatic);
                break;
            case DecoratedElementKind.Setter:
                DefinePrivateMethod(constructor, element.Setter, privateKey, kind: 2, element.IsStatic);
                break;
            default:
                DefinePrivateMethod(constructor, element.Getter, privateKey, kind: 1, element.IsStatic);
                DefinePrivateMethod(constructor, element.Setter, privateKey, kind: 2, element.IsStatic);
                break;
        }
    }

    /// <summary>The get, set or init function returned by an accessor decorator: null when undefined, a TypeError when not callable.</summary>
    private static JsValue? GetDecoratorResultFunction(JsObject result, string name)
    {
        var value = result.Get(new PropertyKey(name));
        if (value.IsCallable)
            return value;

        if (!value.IsUndefined)
            ThrowHelper.ThrowTypeError($"The '{name}' of the object returned by an accessor decorator must be a function or undefined");

        return null;
    }

    /// <summary>DefineMethodProperty(homeObject, methodDefinition, false)</summary>
    private static void DefineMethodProperty(JsObject home, in PropertyKey key, DecoratedElementKind kind, JsValue value, JsValue getter, JsValue setter)
    {
        PropertyDescriptor descriptor;
        switch (kind)
        {
            case DecoratedElementKind.Getter:
                descriptor = new PropertyDescriptor { Get = getter, Flags = PropertyFlags.Configurable, Fields = DescriptorFields.Get | DescriptorFields.Enumerable | DescriptorFields.Configurable };
                break;
            case DecoratedElementKind.Setter:
                descriptor = new PropertyDescriptor { Set = setter, Flags = PropertyFlags.Configurable, Fields = DescriptorFields.Set | DescriptorFields.Enumerable | DescriptorFields.Configurable };
                break;
            case DecoratedElementKind.Accessor:
                descriptor = new PropertyDescriptor { Get = getter, Set = setter, Flags = PropertyFlags.Configurable, Fields = DescriptorFields.Get | DescriptorFields.Set | DescriptorFields.Enumerable | DescriptorFields.Configurable };
                break;
            default:
                descriptor = PropertyDescriptor.Data(value, PropertyFlags.Writable | PropertyFlags.Configurable);
                break;
        }

        home.DefinePropertyOrThrow(key, descriptor);
    }

    /// <summary>The private name of the backing storage of an auto-accessor: "&lt;name&gt; accessor storage".</summary>
    private static PrivateName CreateAutoAccessorStorage(PrivateName? privateKey, in PropertyKey key)
    {
        string readableName;
        if (privateKey is not null)
        {
            readableName = privateKey.Description;
        }
        else if (key.IsSymbol)
        {
            readableName = key.AsSymbol().ToString();
        }
        else
        {
            readableName = key.AsString();
        }

        return new PrivateName(readableName + " accessor storage");
    }

    /// <summary>MakeAutoAccessorGetter and MakeAutoAccessorSetter: built-in functions that read and write the backing storage.</summary>
    private static (JsValue Getter, JsValue Setter) CreateAutoAccessor(JsRealm realm, PropertyKey name, PrivateName storage)
    {
        var getter = realm.CreateBuiltinFunction(name, 0, (_, thisValue, _, _) => PrivateGet(thisValue, storage), prefix: "get");
        var setter = realm.CreateBuiltinFunction(name, 1, (_, thisValue, arguments, _) =>
        {
            PrivateSet(thisValue, storage, arguments.Length > 0 ? arguments[0] : default);
            return default;
        }, prefix: "set");
        return (getter, setter);
    }

    /// <summary>CreateDecoratorContextObject(kind, name, initializers, decorationState, metadata, isStatic): the second argument of a decorator.</summary>
    private static JsObject CreateDecoratorContext(JsRealm realm, DecoratedElementKind? kind, JsValue name, List<JsValue> initializers, DecorationState state, JsObject metadata, bool isStatic)
    {
        var context = new JsObject(realm.Intrinsics.ObjectPrototype);
        var kindName = kind switch
        {
            DecoratedElementKind.Method => "method",
            DecoratedElementKind.Getter => "getter",
            DecoratedElementKind.Setter => "setter",
            DecoratedElementKind.Accessor => "accessor",
            DecoratedElementKind.Field => "field",
            _ => "class",
        };

        context.CreateDataPropertyOrThrow(new PropertyKey("kind"), kindName);
        if (kind is { } elementKind)
        {
            context.CreateDataPropertyOrThrow(new PropertyKey("access"), CreateDecoratorAccess(realm, elementKind, name));
            context.CreateDataPropertyOrThrow(new PropertyKey("static"), isStatic);
            if (name.Cell is PrivateNameCell privateName)
            {
                context.CreateDataPropertyOrThrow(new PropertyKey("private"), JsValue.True);
                context.CreateDataPropertyOrThrow(new PropertyKey("name"), privateName.Name.ToString());
            }
            else
            {
                context.CreateDataPropertyOrThrow(new PropertyKey("private"), JsValue.False);
                context.CreateDataPropertyOrThrow(new PropertyKey("name"), name);
            }
        }
        else
        {
            context.CreateDataPropertyOrThrow(new PropertyKey("name"), name);
        }

        // CreateAddInitializerFunction
        var addInitializer = realm.CreateBuiltinFunction("addInitializer", 1, (_, _, arguments, _) =>
        {
            if (state.Finished)
                ThrowHelper.ThrowTypeError("addInitializer cannot be called after the decorator returned");

            var initializer = arguments.Length > 0 ? arguments[0] : default;
            if (!initializer.IsCallable)
                ThrowHelper.ThrowTypeError("The initializer must be a function");

            initializers.Add(initializer);
            return default;
        });

        context.CreateDataPropertyOrThrow(new PropertyKey("addInitializer"), addInitializer);
        context.CreateDataPropertyOrThrow(new PropertyKey("metadata"), metadata);
        return context;
    }

    /// <summary>CreateDecoratorAccessObject(kind, name): the get, set and has functions of the element, given the object as an argument.</summary>
    private static JsObject CreateDecoratorAccess(JsRealm realm, DecoratedElementKind kind, JsValue name)
    {
        var privateName = (name.Cell as PrivateNameCell)?.Name;
        var key = privateName is null ? JsOperations.ToPropertyKey(name) : default;
        var access = new JsObject(realm.Intrinsics.ObjectPrototype);
        if (kind is not DecoratedElementKind.Setter)
        {
            var getter = realm.CreateBuiltinFunction("", 1, (_, _, arguments, _) =>
            {
                var target = GetDecoratorAccessTarget(arguments);
                return privateName is null ? target.AsObject().Get(key, target) : PrivateGet(target, privateName);
            });

            access.CreateDataPropertyOrThrow(new PropertyKey("get"), getter);
        }

        if (kind is DecoratedElementKind.Field or DecoratedElementKind.Accessor or DecoratedElementKind.Setter)
        {
            var setter = realm.CreateBuiltinFunction("", 2, (_, _, arguments, _) =>
            {
                var target = GetDecoratorAccessTarget(arguments);
                var value = arguments.Length > 1 ? arguments[1] : default;
                if (privateName is null)
                {
                    target.AsObject().Set(key, value, throwOnFailure: true);
                }
                else
                {
                    PrivateSet(target, privateName, value);
                }

                return default;
            });

            access.CreateDataPropertyOrThrow(new PropertyKey("set"), setter);
        }

        var has = realm.CreateBuiltinFunction("has", 1, (_, _, arguments, _) =>
        {
            var target = GetDecoratorAccessTarget(arguments).AsObject();
            if (privateName is null)
                return target.HasProperty(key);

            return target.PrivateElements is { } elements && elements.Find(privateName) >= 0;
        });

        access.CreateDataPropertyOrThrow(new PropertyKey("has"), has);
        return access;
    }

    private static JsValue GetDecoratorAccessTarget(ReadOnlySpan<JsValue> arguments)
    {
        var target = arguments.Length > 0 ? arguments[0] : default;
        if (!target.IsObject)
            ThrowHelper.ThrowTypeError("The target of a decorator access function must be an object");

        return target;
    }
}

/// <summary>A decorator and its receiver (DecoratorDefinition Record).</summary>
internal readonly record struct DecoratorDefinition(JsValue Decorator, JsValue Receiver);

/// <summary>Whether a decorator returned: its addInitializer function throws after that ([[Finished]]).</summary>
internal sealed class DecorationState
{
    public bool Finished { get; set; }
}

/// <summary>The decorators of a class or of a class element, stored in a register or in a hidden binding.</summary>
internal abstract class DecorationCell() : JsCell(JsValueKind.Undefined)
{
    /// <summary>Gets the decorators in source order (they are applied from the last one).</summary>
    public List<DecoratorDefinition> Decorators { get; } = [];
}

/// <summary>The decoration of a class: its decorators, the extra initializers and the metadata object.</summary>
internal sealed class ClassDecorationCell : DecorationCell
{
    /// <summary>Gets or sets the constructor of the class, before the class decorators replace it.</summary>
    public JsClosure? Constructor { get; set; }

    public JsObject? Metadata { get; set; }

    /// <summary>Gets or sets the class returned by the class decorators.</summary>
    public JsObject? Result { get; set; }

    public List<JsValue> StaticMethodInitializers { get; } = [];

    public List<JsValue> InstanceMethodInitializers { get; } = [];

    public List<JsValue> ClassInitializers { get; } = [];
}

/// <summary>The decoration of a class element (ClassElementDefinition Record).</summary>
internal sealed class ElementDecorationCell(DecoratedElementKind kind, bool isStatic, JsObject home) : DecorationCell
{
    public DecoratedElementKind ElementKind { get; } = kind;

    public bool IsStatic { get; } = isStatic;

    /// <summary>Gets the prototype (instance elements) or the constructor (static elements).</summary>
    public JsObject Home { get; } = home;

    /// <summary>Gets the key of a public element.</summary>
    public PropertyKey Key { get; private set; }

    /// <summary>Gets the key of a private element.</summary>
    public PrivateName? PrivateKey { get; private set; }

    public JsValue Value { get; set; }

    public JsValue Getter { get; set; }

    public JsValue Setter { get; set; }

    /// <summary>Gets or sets the initializers added by the decorators of a field or an auto-accessor, after its own initializer.</summary>
    public List<JsValue>? Initializers { get; set; }

    public List<JsValue>? ExtraInitializers { get; set; }

    /// <summary>Sets the key: a private name, or a property key value.</summary>
    public void SetKey(JsValue key)
    {
        if (key.Cell is PrivateNameCell privateName)
        {
            PrivateKey = privateName.Name;
        }
        else
        {
            Key = JsOperations.ToPropertyKey(key);
        }
    }

    /// <summary>The name of the functions of the element: the key, or "#name" for a private element.</summary>
    public PropertyKey GetFunctionName() => PrivateKey is not null ? new PropertyKey(PrivateKey.ToString()) : Key;
}
