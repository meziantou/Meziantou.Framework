using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.JavaScriptEngine.Compiler;

namespace Meziantou.Framework.JavaScriptEngine.Interpreter;

internal static partial class JsInterpreter
{
    // ------------------------------------------------------------------------------------------------------------------
    // Frames
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>Whether a call can push its frame in the current run (an ordinary function: not a generator nor an async function).</summary>
    private static bool CanRunInline(JsClosure closure) => (closure.Template.Flags & (FunctionTemplateFlags.Generator | FunctionTemplateFlags.Async | FunctionTemplateFlags.ClassConstructor)) == 0;

    private static bool CanConstructInline(JsClosure closure) => (closure.Template.Flags & (FunctionTemplateFlags.Generator | FunctionTemplateFlags.Async)) == 0;

    private static Frame PushInlineFrame(Frame caller, Frame callee, JsEngine engine)
    {
        engine.CheckCallDepth();
        engine.CallDepth++;
        callee.Parent = caller;
        engine.PushFrame(callee);
        engine.CurrentRealm = callee.Realm;
        return callee;
    }

    /// <summary>PrepareForOrdinaryCall and OrdinaryCallBindThis: the frame of a call of a closure (from the pool of the engine, except the frames that outlive the call: generators, async functions).</summary>
    private static Frame CreateCallFrame(JsClosure closure, JsValue thisArgument, ReadOnlySpan<JsValue> arguments, JsObject? newTarget, bool pooled = true)
    {
        var template = closure.Template;
        template.EnsureCompiled();
        var realm = closure.Realm;
        var frame = pooled ? realm.Engine.FramePool.Rent(template, realm, closure.Environment, arguments) : new Frame(template, realm, closure.Environment, arguments);
        frame.Function = closure;
        frame.NewTarget = newTarget;
        frame.Script = closure.Script;

        if (!template.IsArrow)
        {
            frame.This = template.IsStrict ? thisArgument : CoerceThis(closure.Realm, thisArgument);
        }

        return frame;
    }

    /// <summary>The this value of a sloppy function: the global this for undefined and null, a wrapper object for the primitives.</summary>
    private static JsValue CoerceThis(JsRealm realm, JsValue thisArgument)
    {
        if (thisArgument.IsNullOrUndefined)
            return realm.GlobalThis;

        if (thisArgument.IsObject)
            return thisArgument;

        return realm.Intrinsics.CreatePrimitiveWrapper(thisArgument);
    }

    /// <summary>The frame of a [[Construct]] of a closure: this is created from new.target (base) or uninitialized (derived).</summary>
    private static Frame CreateConstructFrame(JsClosure closure, ReadOnlySpan<JsValue> arguments, JsObject newTarget)
    {
        JsValue thisValue;
        if (closure.IsDerivedConstructor)
        {
            thisValue = JsValue.Hole;
        }
        else
        {
            var prototype = ObjectOperations.GetPrototypeFromConstructor(newTarget, static intrinsics => intrinsics.ObjectPrototype);
            thisValue = new JsObject(prototype);
        }

        var realm = closure.Realm;
        closure.Template.EnsureCompiled();
        var frame = realm.Engine.FramePool.Rent(closure.Template, realm, closure.Environment, arguments);
        frame.Function = closure;
        frame.NewTarget = newTarget;
        frame.Script = closure.Script;
        frame.This = thisValue;
        frame.IsConstruct = true;
        return frame;
    }

    /// <summary>
    /// The result of a [[Construct]]: the returned object, or this. The errors are created in the realm of the caller, and
    /// thrown as if by the caller (the handlers of the constructor are skipped).
    /// </summary>
    private static JsValue GetConstructResult(Frame frame, JsValue result, JsRealm callerRealm)
    {
        if (result.IsObject)
            return result;

        if (!frame.Function!.IsDerivedConstructor)
            return frame.This;

        if (result.IsUndefined)
        {
            // The this binding is in the frame (or, when it is in the environment, the compiler has replaced undefined by it)
            result = frame.This;
        }
        else if (!result.IsHole)
        {
            ThrowFromCaller(frame, callerRealm, ErrorType.TypeError, "Derived constructors may only return object or undefined");
        }

        if (result.IsHole)
        {
            ThrowFromCaller(frame, callerRealm, ErrorType.ReferenceError, "Must call super constructor in derived class before accessing 'this' or returning from derived constructor");
        }

        return result;

        [DoesNotReturn]
        static void ThrowFromCaller(Frame frame, JsRealm callerRealm, ErrorType type, string message)
        {
            frame.Pc = -1;
            throw new JsException(new JsValue(callerRealm.CreateError(type, message)));
        }
    }

    /// <summary>[[Call]] of a closure from .NET (a built-in calls a callback...).</summary>
    public static JsValue CallClosure(JsClosure closure, JsValue thisArgument, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var template = closure.Template;
        var engine = closure.Realm.Engine;
        if (template.IsGenerator || template.IsAsync)
            return StartGeneratorOrAsyncFunction(closure, thisArgument, arguments);

        engine.CheckCallDepth();
        engine.CallDepth++;
        try
        {
            var frame = CreateCallFrame(closure, thisArgument, arguments, newTarget);
            return Run(frame);
        }
        finally
        {
            engine.CallDepth--;
        }
    }

    /// <summary>[[Construct]] of a closure from .NET.</summary>
    public static JsValue ConstructClosure(JsClosure closure, ReadOnlySpan<JsValue> arguments, JsObject newTarget)
    {
        var engine = closure.Realm.Engine;
        engine.CheckCallDepth();
        engine.CallDepth++;
        try
        {
            var previousRealm = engine.CurrentRealm;
            engine.CurrentRealm = closure.Realm;
            Frame frame;
            try
            {
                frame = CreateConstructFrame(closure, arguments, newTarget);
            }
            finally
            {
                engine.CurrentRealm = previousRealm;
            }

            return Run(frame);
        }
        finally
        {
            engine.CallDepth--;
        }
    }

    private static void ThrowNotCallable(Frame frame, JsValue value)
    {
        ThrowHelper.ThrowTypeError($"{DescribeCallee(frame)} is not a function");
        _ = value;
    }

    /// <summary>Describes the callee of the current call for the error messages (the source text of the call, e.g. "obj.method").</summary>
    private static string DescribeCallee(Frame frame)
    {
        var text = frame.Code.Source.Text;
        if (frame.Code.TryGetCallSite(frame.Pc, out var calleeStart, out var calleeEnd) && calleeStart >= 0 && calleeEnd <= text.Length && calleeStart < calleeEnd)
            return DescribeCallee(text.AsSpan(calleeStart, calleeEnd - calleeStart));

        var offset = frame.Code.GetSourceOffset(frame.Pc);
        if (offset < 0 || offset >= text.Length)
            return "expression";

        var end = offset;
        var depth = 0;
        while (end < text.Length)
        {
            var c = text[end];
            if (c is '(' or '`' && depth == 0)
                break;

            if (c == '[')
            {
                depth++;
            }
            else if (c == ']')
            {
                depth--;
            }
            else if (c is ';' or '\n' or '\r' or ',' or ')' or '{' or '}')
            {
                break;
            }

            end++;
        }

        var description = text[offset..end].Trim();
        if (description.StartsWith("new ", StringComparison.Ordinal))
        {
            description = description[4..].Trim();
        }

        return description.Length is > 0 and < 100 ? description : "expression";
    }

    /// <summary>The source text of a callee, with the arguments of the calls it contains replaced by "(...)" (e.g. "a.b(...).c").</summary>
    private static string DescribeCallee(ReadOnlySpan<char> callee)
    {
        var builder = new StringBuilder(callee.Length);
        for (var i = 0; i < callee.Length; i++)
        {
            var c = callee[i];
            // The arguments of a call (a parenthesized expression is kept)
            if (c == '(' && IsEndOfCallee(builder))
            {
                i = SkipBalanced(callee, i);
                builder.Append("(...)");
                continue;
            }

            if (c is '\r' or '\n' or '\t')
            {
                c = ' ';
            }

            builder.Append(c);
        }

        var description = builder.ToString().Trim();
        return description.Length is > 0 and < 100 ? description : "expression";

        static bool IsEndOfCallee(StringBuilder builder)
        {
            for (var i = builder.Length - 1; i >= 0; i--)
            {
                var c = builder[i];
                if (c == ' ')
                    continue;

                return c is ')' or ']' or '_' or '$' || char.IsAsciiLetterOrDigit(c) || c > '\u007F';
            }

            return false;
        }

        // The index of the parenthesis that closes the one at start (strings and templates are skipped)
        static int SkipBalanced(ReadOnlySpan<char> text, int start)
        {
            var depth = 0;
            for (var i = start; i < text.Length; i++)
            {
                switch (text[i])
                {
                    case '(':
                        depth++;
                        break;
                    case ')':
                        depth--;
                        if (depth == 0)
                            return i;

                        break;
                    case '\'' or '"' or '`':
                        var quote = text[i];
                        for (i++; i < text.Length && text[i] != quote; i++)
                        {
                            if (text[i] == '\\')
                            {
                                i++;
                            }
                        }

                        break;
                }
            }

            return text.Length - 1;
        }
    }

    private static JsValue[] GetSpreadArguments(JsValue array)
    {
        var jsArray = (JsArray)array.Cell!;
        var result = new JsValue[jsArray.Length];
        for (var i = 0u; i < jsArray.Length; i++)
        {
            result[i] = jsArray.Get(new PropertyKey(i));
        }

        return result;
    }

    private static void AppendSpread(JsArray array, JsValue iterable)
    {
        var record = IteratorOperations.GetIterator(iterable);
        while (IteratorOperations.IteratorStepValue(record, out var value))
        {
            array.Push(value);
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // super
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// super(...args) (https://tc39.es/ecma262/#sec-super-keyword-runtime-semantics-evaluation): the result is the new this.
    /// The super constructor (GetSuperConstructor) is read before the arguments are evaluated.
    /// </summary>
    private static JsValue SuperCall(JsValue superConstructor, JsValue newTarget, JsValue[] arguments)
    {
        if (superConstructor.Cell is not JsObject { IsConstructor: true } constructor)
            return ThrowHelper.ThrowTypeError<JsValue>("Super constructor " + (superConstructor.IsNull ? "null" : "") + " of anonymous class is not a constructor");

        return constructor.Construct(arguments, newTarget.AsObject());
    }

    /// <summary>The object on which super.x is looked up (the prototype of the home object): ToObject of the base of the reference.</summary>
    private static JsObject ToSuperBaseObject(JsValue superBase)
    {
        if (superBase.Cell is not JsObject baseObject)
            return ThrowHelper.ThrowTypeError<JsObject>("Cannot read properties of null (super)");

        return baseObject;
    }

    private static void SetSuper(JsObject superBase, in PropertyKey key, JsValue value, JsValue thisValue, bool strict)
    {
        if (!superBase.Set(key, value, thisValue) && strict)
        {
            ThrowHelper.ThrowTypeError($"Cannot assign to read only property '{key}' of object");
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Closures
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>InstantiateFunctionObject / InstantiateOrdinaryFunctionExpression and the other kinds of functions.</summary>
    private static JsValue CreateClosure(Frame frame, FunctionTemplate template)
    {
        return CreateClosure(frame.Realm, template, frame.Environment, frame.Script);
    }

    public static JsClosure CreateClosure(JsRealm realm, FunctionTemplate template, Environment environment, IScriptOrModule? script)
    {
        var intrinsics = realm.Intrinsics;
        var prototype = (template.Flags & (FunctionTemplateFlags.Async | FunctionTemplateFlags.Generator)) switch
        {
            FunctionTemplateFlags.Generator => intrinsics.GeneratorFunctionPrototype,
            FunctionTemplateFlags.Async => intrinsics.AsyncFunctionPrototype,
            FunctionTemplateFlags.Async | FunctionTemplateFlags.Generator => intrinsics.AsyncGeneratorFunctionPrototype,
            _ => intrinsics.FunctionPrototype,
        };

        // The environment of the name of a named function expression holds the function itself
        DeclarativeEnvironment? nameEnvironment = null;
        if (template.NameScope is { } nameScope)
        {
            nameEnvironment = new DeclarativeEnvironment(nameScope, environment);
            environment = nameEnvironment;
        }

        var closure = new JsClosure(realm, template, environment, prototype, script);
        closure.SetFunctionLength(template.Length);
        closure.SetFunctionName(template.NameValue);
        nameEnvironment?.Slots[0] = closure;

        if (template.IsGenerator)
        {
            // The prototype of the generator objects (not a constructor: no constructor property)
            var generatorPrototype = new JsObject(template.IsAsync ? intrinsics.AsyncGeneratorPrototype : intrinsics.GeneratorPrototype);
            closure.DefineBuiltIn("prototype", generatorPrototype, PropertyFlags.Writable);
        }
        else if ((template.Flags & FunctionTemplateFlags.Constructor) != 0 && !template.IsClassConstructor)
        {
            // Non-strict functions have caller and arguments own properties, accessors that read the running frames: an
            // extension the spec allows, that the browsers implement
            if (!template.IsStrict)
            {
                closure.DefineBuiltInAccessor("arguments", intrinsics.FunctionArgumentsProperty, PropertyFlags.None);
                closure.DefineBuiltInAccessor("caller", intrinsics.FunctionCallerProperty, PropertyFlags.None);
            }

            // MakeConstructor
            var functionPrototype = new JsObject(intrinsics.ObjectPrototype);
            functionPrototype.DefineBuiltIn("constructor", closure);
            closure.DefineBuiltIn("prototype", functionPrototype, PropertyFlags.Writable);
        }

        return closure;
    }

    private static void SetFunctionNameAtRuntime(JsObject function, JsValue name, int prefix)
    {
        var key = name.IsSymbol ? new PropertyKey(name.AsSymbol()) : JsOperations.ToPropertyKey(name);
        string? prefixText = prefix switch
        {
            1 => "get",
            2 => "set",
            _ => null,
        };

        if (function is JsFunction jsFunction)
        {
            // The name is defined once: SetFunctionName replaces the empty name of the anonymous function
            jsFunction.SetFunctionName(key, prefixText);
        }
    }

    /// <summary>Defines a method, a getter or a setter of an object literal or a class (DefineMethodProperty).</summary>
    private static void DefineMethod(JsObject target, in PropertyKey key, JsClosure function, int kind, bool enumerable)
    {
        function.HomeObject = target;
        var flags = enumerable ? PropertyFlags.Enumerable | PropertyFlags.Configurable : PropertyFlags.Configurable;
        PropertyDescriptor descriptor;
        switch (kind)
        {
            case 1:
                descriptor = new PropertyDescriptor { Get = function, Flags = flags, Fields = DescriptorFields.Get | DescriptorFields.Enumerable | DescriptorFields.Configurable };
                break;
            case 2:
                descriptor = new PropertyDescriptor { Set = function, Flags = flags, Fields = DescriptorFields.Set | DescriptorFields.Enumerable | DescriptorFields.Configurable };
                break;
            default:
                descriptor = PropertyDescriptor.Data(function, flags | PropertyFlags.Writable);
                break;
        }

        target.DefinePropertyOrThrow(key, descriptor);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Classes
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>The first steps of ClassDefinitionEvaluation: the prototype and the constructor.</summary>
    private static (JsValue Constructor, JsValue Prototype) CreateClass(Frame frame, ClassTemplate template, JsValue superclass)
    {
        var realm = frame.Realm;
        var intrinsics = realm.Intrinsics;
        JsObject? protoParent;
        JsObject constructorParent;
        if (superclass.IsHole)
        {
            protoParent = intrinsics.ObjectPrototype;
            constructorParent = intrinsics.FunctionPrototype;
        }
        else if (superclass.IsNull)
        {
            protoParent = null;
            constructorParent = intrinsics.FunctionPrototype;
        }
        else
        {
            if (superclass.Cell is not JsObject { IsConstructor: true } superConstructor)
                throw ThrowHelper.CreateException(ErrorType.TypeError, "Class extends value " + JsOperations.DescribeForError(superclass) + " is not a constructor or null");

            var superPrototype = superConstructor.Get(new PropertyKey("prototype"));
            if (superPrototype.Cell is JsObject superPrototypeObject)
            {
                protoParent = superPrototypeObject;
            }
            else if (superPrototype.IsNull)
            {
                protoParent = null;
            }
            else
            {
                throw ThrowHelper.CreateException(ErrorType.TypeError, "Class extends value does not have valid prototype property " + JsOperations.DescribeForError(superPrototype));
            }

            constructorParent = superConstructor;
        }

        var prototype = new JsObject(protoParent);
        var constructorTemplate = template.Constructor!;
        var constructor = new JsClosure(realm, constructorTemplate, frame.Environment, constructorParent, frame.Script)
        {
            HomeObject = prototype,
            ClassDefinition = template,
        };

        constructor.SetFunctionLength(constructorTemplate.Length);
        constructor.SetFunctionName(template.Name);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);
        prototype.DefineBuiltIn("constructor", constructor);
        return (constructor, prototype);
    }

    private static void DefinePrivateMethod(JsClosure constructor, JsObject home, JsClosure function, PrivateName name, int kind, bool isStatic)
    {
        function.HomeObject = home;
        var target = isStatic ? null : constructor.PrivateMethods ??= [];
        var record = kind switch
        {
            1 => new PrivateMethodRecord(name, PrivateElementKind.Accessor, default, function, default),
            2 => new PrivateMethodRecord(name, PrivateElementKind.Accessor, default, default, function),
            _ => new PrivateMethodRecord(name, PrivateElementKind.Method, function, default, default),
        };

        if (isStatic)
        {
            // A static getter and setter of the same name are one element of the class
            if (record.Kind == PrivateElementKind.Accessor && constructor.PrivateElements is { } staticElements && staticElements.Find(name) is var staticIndex && staticIndex >= 0)
            {
                staticElements.UpdateAccessor(staticIndex, record.Getter, record.Setter);
                return;
            }

            PrivateMethodOrAccessorAdd(constructor, record);
            return;
        }

        // A getter and a setter of the same name are one element
        var existing = target!.FindIndex(r => ReferenceEquals(r.Name, name));
        if (existing >= 0 && record.Kind == PrivateElementKind.Accessor)
        {
            var previous = target[existing];
            target[existing] = previous with
            {
                Getter = record.Getter.IsUndefined ? previous.Getter : record.Getter,
                Setter = record.Setter.IsUndefined ? previous.Setter : record.Setter,
            };

            return;
        }

        target.Add(record);
    }

    /// <summary>InitializeInstanceElements(O, constructor): the private methods, then the fields.</summary>
    public static void InitializeInstanceElements(JsObject obj, JsClosure constructor)
    {
        if (constructor.PrivateMethods is { Count: > 0 } methods)
        {
            // Non-extensible Applies to Private (https://github.com/tc39/proposal-nonextensible-applies-to-private)
            if (!obj.IsExtensible())
            {
                ThrowHelper.ThrowTypeError("Cannot add private methods to a non-extensible object");
            }

            foreach (var method in methods)
            {
                PrivateMethodOrAccessorAdd(obj, method);
            }
        }

        constructor.FieldInitializer?.Call(obj, []);
    }

    private static void PrivateMethodOrAccessorAdd(JsObject obj, PrivateMethodRecord method)
    {
        var elements = obj.PrivateElements ??= new PrivateElementTable();
        var index = elements.Find(method.Name);
        if (index >= 0)
        {
            ThrowHelper.ThrowTypeError("Cannot initialize private methods of class twice on the same object");
        }

        if (method.Kind == PrivateElementKind.Method)
        {
            elements.AddMethod(method.Name, method.Method);
        }
        else
        {
            elements.AddAccessor(method.Name, method.Getter, method.Setter);
        }
    }

    private static void PrivateFieldAdd(JsObject obj, PrivateName name, JsValue value)
    {
        // Non-extensible Applies to Private (https://github.com/tc39/proposal-nonextensible-applies-to-private)
        if (!obj.IsExtensible())
        {
            ThrowHelper.ThrowTypeError($"Cannot add {name} to a non-extensible object");
        }

        var elements = obj.PrivateElements ??= new PrivateElementTable();
        if (elements.Contains(name))
        {
            ThrowHelper.ThrowTypeError($"Cannot initialize {name} twice on the same object");
        }

        elements.AddField(name, value);
    }

    /// <summary>Gets a private name: from the top of the stack when the depth is negative (the caller pops it), else from an environment slot.</summary>
    private static PrivateName GetPrivateName(Frame frame, JsValue[] values, int sp, int depth, int slot)
    {
        if (depth < 0)
            return ((PrivateNameCell)values[sp - 1].Cell!).Name;

        return ((PrivateNameCell)GetEnvironment(frame.Environment, depth).Slots[slot].Cell!).Name;
    }

    /// <summary>PrivateGet(O, P)</summary>
    private static JsValue PrivateGet(JsValue value, PrivateName name)
    {
        if (value.Cell is not JsObject obj || obj.PrivateElements is not { } elements || elements.Find(name) is var index && index < 0)
            return ThrowHelper.ThrowTypeError<JsValue>($"Cannot read private member {name} from an object whose class did not declare it");

        switch (elements.GetKind(index))
        {
            case PrivateElementKind.Field:
            case PrivateElementKind.Method:
                return elements.GetValue(index);
            default:
                var getter = elements.GetGetter(index);
                if (getter.IsUndefined)
                    return ThrowHelper.ThrowTypeError<JsValue>($"'{name}' was defined without a getter");

                return getter.AsObject().Call(value, []);
        }
    }

    /// <summary>PrivateSet(O, P, value)</summary>
    private static void PrivateSet(JsValue value, PrivateName name, JsValue newValue)
    {
        if (value.Cell is not JsObject obj || obj.PrivateElements is not { } elements || elements.Find(name) is var index && index < 0)
        {
            ThrowHelper.ThrowTypeError($"Cannot write private member {name} to an object whose class did not declare it");
            return;
        }

        switch (elements.GetKind(index))
        {
            case PrivateElementKind.Field:
                elements.SetValue(index, newValue);
                break;
            case PrivateElementKind.Method:
                ThrowHelper.ThrowTypeError($"Private method {name} is not writable");
                break;
            default:
                var setter = elements.GetSetter(index);
                if (setter.IsUndefined)
                {
                    ThrowHelper.ThrowTypeError($"'{name}' was defined without a setter");
                }

                setter.AsObject().Call(value, [newValue]);
                break;
        }
    }
}

/// <summary>A private name stored in an environment slot.</summary>
internal sealed class PrivateNameCell(PrivateName name) : JsCell(JsValueKind.Undefined)
{
    public PrivateName Name { get; } = name;
}

/// <summary>An iterator record stored in a register or on the stack.</summary>
internal sealed class IteratorRecordCell(IteratorRecord record) : JsCell(JsValueKind.Undefined)
{
    public IteratorRecord Record { get; } = record;
}
