using System.Runtime.CompilerServices;
using Meziantou.Framework.JavaScriptEngine.Compiler;

namespace Meziantou.Framework.JavaScriptEngine.Interpreter;

/// <summary>
/// The instructions executed outside the dispatch loop of <see cref="JsInterpreter.Execute"/>: the slow paths of the hot
/// instructions, and the less frequent instructions (one method each, called by <see cref="JsInterpreter.ExecuteCold"/>:
/// frame.Pc is the position of the instruction, they set the position of the next instruction and return the new
/// stack pointer).
/// </summary>
internal static partial class JsInterpreter
{
    // ------------------------------------------------------------------------------------------------------------------
    // Slow paths of the dispatch loop
    // ------------------------------------------------------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowTdz(Frame frame, int name) => ThrowTdz(frame.Code.Keys[name]);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowThisNotInitialized() => ThrowHelper.ThrowReferenceError("Must call super constructor in derived class before accessing 'this' or returning from derived constructor");


    private static void SetElement(Frame frame, JsValue target, JsValue keyValue, JsValue value)
    {
        if (target.IsNullOrUndefined)
        {
            ThrowHelper.ThrowTypeError($"Cannot set properties of {target}{DescribeKeyForError(keyValue, "setting")}");
        }

        var key = keyValue.IsNumber ? PropertyKey.FromDouble(keyValue.NumberValue) : JsOperations.ToPropertyKey(keyValue);
        SetPropertyOfValue(target, key, value, frame.Code.IsStrict);
    }


    private static void ArrayAppend(JsValue target, JsValue value)
    {
        var array = (JsArray)target.Cell!;
        if (value.IsHole)
        {
            array.DefineOwnProperty(new PropertyKey("length"), PropertyDescriptor.ValueOnly(array.Length + 1));
        }
        else
        {
            array.Push(value);
        }
    }

    /// <summary>A call that does not push a frame in the current run (a built-in function, a generator, a proxy...).</summary>
    private static JsValue CallValue(Frame frame, JsValue function, JsValue thisValue, ReadOnlySpan<JsValue> arguments)
    {
        if (function.Cell is not JsObject { IsCallable: true } callable)
        {
            ThrowNotCallable(frame, function);
            return default;
        }

        return callable.Call(thisValue, arguments);
    }

    private static JsObject GetConstructor(Frame frame, JsValue value)
    {
        if (value.Cell is not JsObject { IsConstructor: true } constructor)
            return ThrowHelper.ThrowTypeError<JsObject>($"{DescribeCallee(frame)} is not a constructor");

        return constructor;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Instructions
    // ------------------------------------------------------------------------------------------------------------------

    private static int ExecuteRot3Reverse(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        // a b c -> b c a
        var a = values[sp - 3];
        values[sp - 3] = values[sp - 2];
        values[sp - 2] = values[sp - 1];
        values[sp - 1] = a;
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteRot4(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        // a b c d -> d a b c
        var d = values[sp - 1];
        values[sp - 1] = values[sp - 2];
        values[sp - 2] = values[sp - 3];
        values[sp - 3] = values[sp - 4];
        values[sp - 4] = d;
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteDup3(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        values[sp] = values[sp - 3];
        values[sp + 1] = values[sp - 2];
        values[sp + 2] = values[sp - 1];
        sp += 3;
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteRot5(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        // a b c d e -> e a b c d
        var e = values[sp - 1];
        values[sp - 1] = values[sp - 2];
        values[sp - 2] = values[sp - 3];
        values[sp - 3] = values[sp - 4];
        values[sp - 4] = values[sp - 5];
        values[sp - 5] = e;
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteLoadImport(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        var depth = code[pc++];
        var slot = code[pc++];
        var name = code[pc++];
        values[sp++] = ImportBindingCell.Read(GetEnvironment(frame.Environment, depth).Slots[slot], frame.Code.Keys[name]);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteThrowConstAssignLocal(Frame frame)
    {
        var code = frame.Code.Code;
        var register = code[frame.Pc + 1];
        if (register >= 0 && frame.Values[register].IsHole)
        {
            ThrowTdz(frame, code[frame.Pc + 2]);
        }

        throw ThrowHelper.CreateException(ErrorType.TypeError, "Assignment to constant variable.");
    }

    private static int ExecuteThrowConstAssignContext(Frame frame)
    {
        var code = frame.Code.Code;
        var depth = code[frame.Pc + 1];
        var slot = code[frame.Pc + 2];
        if (slot >= 0 && GetEnvironment(frame.Environment, depth).Slots[slot].IsHole)
        {
            ThrowTdz(frame, code[frame.Pc + 3]);
        }

        throw ThrowHelper.CreateException(ErrorType.TypeError, "Assignment to constant variable.");
    }

    private static int ExecuteLoadGlobalOrUndefined(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        values[sp++] = LoadGlobal(frame.Realm, frame.Code.Keys[code[pc]], throwIfMissing: false);
        pc += 2;
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteStoreGlobal(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        var name = frame.Code.Keys[code[pc++]];
        var isStrict = code[pc++] != 0;
        StoreGlobal(frame.Realm, name, values[--sp], isStrict);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteLoadName(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        values[sp++] = LoadName(frame, frame.Code.Keys[code[pc++]], throwIfMissing: true, out _);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteLoadNameOrUndefined(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        values[sp++] = LoadName(frame, frame.Code.Keys[code[pc++]], throwIfMissing: false, out _);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteLoadNameForCall(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        values[sp++] = LoadName(frame, frame.Code.Keys[code[pc++]], throwIfMissing: true, out var thisValue);
        values[sp++] = thisValue;
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteStoreName(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        var name = frame.Code.Keys[code[pc++]];
        var isStrict = code[pc++] != 0;
        StoreName(frame, name, values[--sp], isStrict);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteDeleteName(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        values[sp++] = DeleteName(frame, frame.Code.Keys[code[pc++]]);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteInitializeName(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        InitializeName(frame, frame.Code.Keys[code[pc++]], values[--sp]);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecutePushWith(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        frame.Environment = new ObjectEnvironment(values[--sp].AsObject(), frame.Environment);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteSaveEnvironment(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        values[code[pc++]] = new JsValue(frame.Environment);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteRestoreEnvironment(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        frame.Environment = (Environment)values[code[pc++]].Cell!;
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteLoadRestArguments(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        var index = code[pc++];
        var arguments = frame.Arguments;
        values[sp++] = JsArray.Create(frame.Realm, index < arguments.Length ? arguments[index..] : []);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteLoadAllArguments(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        values[sp++] = JsArray.Create(frame.Realm, frame.Arguments);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteCreateArguments(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        values[sp++] = CreateArgumentsObject(frame, mapped: code[pc++] != 0);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteLoadFunction(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        values[sp++] = frame.Function is { } currentFunction ? new JsValue(currentFunction) : default;
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteLoadNewTarget(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        values[sp++] = frame.NewTarget is { } currentNewTarget ? new JsValue(currentNewTarget) : default;
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteLoadHomeObject(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        values[sp++] = frame.Function?.HomeObject is { } currentHome ? new JsValue(currentHome) : default;
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteLoadGlobalThis(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        values[sp++] = frame.Realm.GlobalThis;
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteLoadImportMeta(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        values[sp++] = GetImportMeta(frame);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteBindThis(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        if (!frame.This.IsHole)
        {
            ThrowHelper.ThrowReferenceError("Super constructor may only be called once");
        }

        frame.This = values[sp - 1];
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteCheckThisUninitialized(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        if (!values[sp - 1].IsHole)
        {
            ThrowHelper.ThrowReferenceError("Super constructor may only be called once");
        }

        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteBinary(Frame frame, int sp, OpCode op)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        var left = values[sp - 2];
        var right = values[sp - 1];
        sp--;
        values[sp - 1] = Operators.Binary(op, left, right);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteInstanceOf(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        sp--;
        values[sp - 1] = JsValue.FromBoolean(ObjectOperations.InstanceOf(values[sp - 1], values[sp]));
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteIn(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        sp--;
        var target = values[sp];
        if (target.Cell is not JsObject targetObject)
        {
            throw ThrowHelper.CreateException(ErrorType.TypeError, "Cannot use 'in' operator to search for a key in " + JsOperations.DescribeForError(target));
        }

        values[sp - 1] = JsValue.FromBoolean(targetObject.HasProperty(JsOperations.ToPropertyKey(values[sp - 1])));
        frame.Pc = pc;
        return sp;
    }

    private static int ExecutePrivateIn(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        var depth = code[pc++];
        var slot = code[pc++];
        var privateName = GetPrivateName(frame, values, sp, depth, slot);
        if (depth < 0)
        {
            sp--;
        }

        var target = values[sp - 1];
        if (target.Cell is not JsObject targetObject)
        {
            throw ThrowHelper.CreateException(ErrorType.TypeError, "Cannot use 'in' operator to search for a private field in a non-object");
        }

        values[sp - 1] = JsValue.FromBoolean(targetObject.PrivateElements?.Contains(privateName) == true);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteToNumber(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        values[sp - 1] = new JsValue(JsOperations.ToNumber(values[sp - 1]));
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteBitwiseNot(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        values[sp - 1] = Operators.BitwiseNot(values[sp - 1]);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteToStringValue(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        values[sp - 1] = JsValue.FromJsString(JsOperations.ToJsString(values[sp - 1]));
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteToPropertyKey(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        var value = values[sp - 1];
        if (!value.IsString && !value.IsSymbol && !value.IsNumber)
        {
            values[sp - 1] = JsOperations.ToPropertyKey(value).ToJsValue();
        }

        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteToObject(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        values[sp - 1] = JsOperations.ToObject(values[sp - 1]);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteRequireObjectCoercible(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        if (values[sp - 1].IsNullOrUndefined)
        {
            ThrowHelper.ThrowTypeError($"Cannot destructure '{values[sp - 1]}' as it is {values[sp - 1]}.");
        }

        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteRequireObjectCoercibleBase(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        if (values[sp - 2].IsNullOrUndefined)
        {
            ThrowHelper.ThrowTypeError($"Cannot read properties of {values[sp - 2]}");
        }

        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteResolveName(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        values[sp++] = ResolveName(frame, frame.Code.Keys[code[pc++]]);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteLoadResolved(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        values[sp - 1] = LoadResolved(frame, values[sp - 1], frame.Code.Keys[code[pc++]]);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteStoreResolved(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        var name = frame.Code.Keys[code[pc++]];
        var isStrict = code[pc++] != 0;
        var value = values[--sp];
        StoreResolved(frame, values[sp - 1], name, value, isStrict);
        values[sp - 1] = value;
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteJumpIfNullishKeep(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        var target = code[pc++];
        if (values[sp - 1].IsNullOrUndefined)
        {
            pc = target;
        }

        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteJumpIfNotNullishKeep(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        var target = code[pc++];
        if (!values[sp - 1].IsNullOrUndefined)
        {
            pc = target;
        }

        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteJumpIfNotUndefinedKeep(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        var target = code[pc++];
        if (!values[sp - 1].IsUndefined)
        {
            pc = target;
        }

        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteJumpIfUndefinedPop(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        var target = code[pc++];
        if (values[sp - 1].IsUndefined)
        {
            sp--;
            pc = target;
        }

        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteThrowError(Frame frame)
    {
        var code = frame.Code.Code;
        var type = (ErrorType)code[frame.Pc + 1];
        var message = frame.Code.Constants[code[frame.Pc + 2]].AsString();
        throw ThrowHelper.CreateException(type, message);
    }

    private static int ExecuteDebugger(Frame frame, int sp)
    {
        frame.Pc++;
        return sp;
    }

    private static int ExecuteArrayAppendSpread(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        var iterable = values[--sp];
        frame.Sp = sp;
        AppendSpread((JsArray)values[sp - 1].Cell!, iterable);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteDeleteProperty(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        var key = frame.Code.Keys[code[pc++]];
        var isStrict = code[pc++] != 0;
        values[sp - 1] = DeleteProperty(values[sp - 1], key, isStrict);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteDeleteElement(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        var isStrict = code[pc++] != 0;
        var keyValue = values[--sp];
        var target = values[sp - 1];
        var targetObject = JsOperations.ToObject(target);
        values[sp - 1] = DeleteProperty(targetObject, JsOperations.ToPropertyKey(keyValue), isStrict);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteDefineFieldComputed(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        var value = values[--sp];
        var key = JsOperations.ToPropertyKey(values[--sp]);
        ((JsObject)values[sp - 1].Cell!).CreateDataPropertyOrThrow(key, value);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteDefineMethod(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        var kind = code[pc++];
        var enumerable = code[pc++] != 0;
        var function = (JsClosure)values[--sp].Cell!;
        var key = JsOperations.ToPropertyKey(values[--sp]);
        var target = (JsObject)values[sp - 1].Cell!;
        DefineMethod(target, key, function, kind, enumerable);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteSetPrototypeLiteral(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        var value = values[--sp];
        var target = (JsObject)values[sp - 1].Cell!;
        if (value.Cell is JsObject prototype)
        {
            target.SetPrototypeOf(prototype);
        }
        else if (value.IsNull)
        {
            target.SetPrototypeOf(null);
        }

        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteCopyDataProperties(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        var source = values[--sp];
        PropertyOperations.CopyDataProperties((JsObject)values[sp - 1].Cell!, source, []);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteCopyDataPropertiesExcluding(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        var count = code[pc++];
        var excluded = new PropertyKey[count];
        for (var i = count - 1; i >= 0; i--)
        {
            excluded[i] = JsOperations.ToPropertyKey(values[--sp]);
        }

        var source = values[sp - 1];
        var result = new JsObject(frame.Realm.Intrinsics.ObjectPrototype);
        PropertyOperations.CopyDataProperties(result, source, excluded);
        values[sp - 1] = result;
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteLoadSuperBase(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        values[sp - 1] = values[sp - 1].AsObject().GetPrototypeOf() is { } superPrototype ? new JsValue(superPrototype) : JsValue.Null;
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteGetSuperProperty(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        var key = frame.Code.Keys[code[pc++]];
        var superBase = ToSuperBaseObject(values[--sp]);
        var thisValue = values[sp - 1];
        values[sp - 1] = superBase.Get(key, thisValue);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteGetSuperElement(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        var superBase = ToSuperBaseObject(values[--sp]);
        var key = JsOperations.ToPropertyKey(values[--sp]);
        var thisValue = values[sp - 1];
        values[sp - 1] = superBase.Get(key, thisValue);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteSetSuperProperty(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        var key = frame.Code.Keys[code[pc++]];
        var value = values[--sp];
        var superBase = ToSuperBaseObject(values[--sp]);
        var thisValue = values[sp - 1];
        SetSuper(superBase, key, value, thisValue, frame.Code.IsStrict);
        values[sp - 1] = value;
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteSetSuperElement(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        var value = values[--sp];
        var superBase = ToSuperBaseObject(values[--sp]);
        var key = JsOperations.ToPropertyKey(values[--sp]);
        var thisValue = values[sp - 1];
        SetSuper(superBase, key, value, thisValue, frame.Code.IsStrict);
        values[sp - 1] = value;
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteGetPrivate(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        var depth = code[pc++];
        var slot = code[pc++];
        var privateName = GetPrivateName(frame, values, sp, depth, slot);
        if (depth < 0)
        {
            sp--;
        }

        values[sp - 1] = PrivateGet(values[sp - 1], privateName);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteSetPrivate(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        var depth = code[pc++];
        var slot = code[pc++];
        var privateName = GetPrivateName(frame, values, sp, depth, slot);
        if (depth < 0)
        {
            sp--;
        }

        var value = values[--sp];
        PrivateSet(values[sp - 1], privateName, value);
        values[sp - 1] = value;
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteCallEval(Frame frame, int sp, OpCode op)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        JsValue function;
        JsValue thisValue;
        JsValue[] arguments;
        EvalContext context;
        if (op == OpCode.CallEval)
        {
            var argumentCount = code[pc++];
            context = (EvalContext)frame.Code.Objects[code[pc++]];
            var functionIndex = sp - argumentCount - 2;
            function = values[functionIndex];
            thisValue = values[functionIndex + 1];
            arguments = values.AsSpan(functionIndex + 2, argumentCount).ToArray();
            sp = functionIndex;
        }
        else
        {
            context = (EvalContext)frame.Code.Objects[code[pc++]];
            arguments = GetSpreadArguments(values[--sp]);
            thisValue = values[--sp];
            function = values[--sp];
        }

        frame.Sp = sp;
        values[sp++] = CallEval(frame, function, thisValue, arguments, context);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteSuperCall(Frame frame, int sp, OpCode op)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        JsValue[] arguments;
        if (op == OpCode.SuperCall)
        {
            var argumentCount = code[pc++];
            arguments = values.AsSpan(sp - argumentCount, argumentCount).ToArray();
            sp -= argumentCount;
        }
        else
        {
            arguments = GetSpreadArguments(values[--sp]);
        }

        var newTarget = values[--sp];
        var activeFunction = values[--sp];
        frame.Sp = sp;
        values[sp++] = SuperCall(activeFunction, newTarget, arguments);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteImportCall(Frame frame, int sp, Modules.ModulePhase phase = Modules.ModulePhase.Evaluation)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        var options = values[--sp];
        var specifier = values[--sp];
        frame.Sp = sp;
        values[sp++] = ImportCall(frame, specifier, options, phase);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteCreateClass(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        var classTemplate = (ClassTemplate)frame.Code.Objects[code[pc++]];
        var superclass = values[sp - 1];
        var (constructor, prototype) = CreateClass(frame, classTemplate, superclass);
        values[sp - 1] = constructor;
        values[sp++] = prototype;
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteInitializeInstanceElements(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        var constructor = (JsClosure)values[--sp].Cell!;
        InitializeInstanceElements((JsObject)values[sp - 1].Cell!, constructor);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteCreatePrivateName(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        values[sp++] = new JsValue(new PrivateNameCell(new PrivateName(frame.Code.Constants[code[pc++]].AsString()[1..])));
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteDefinePrivateMethod(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        var kind = code[pc++];
        var isStatic = code[pc++] != 0;
        var privateName = ((PrivateNameCell)values[--sp].Cell!).Name;
        var function = (JsClosure)values[--sp].Cell!;
        var home = (JsObject)values[--sp].Cell!;
        var constructor = (JsClosure)values[--sp].Cell!;
        DefinePrivateMethod(constructor, home, function, privateName, kind, isStatic);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteSetFieldInitializer(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        var initializer = (JsClosure)values[--sp].Cell!;
        var constructor = (JsClosure)values[--sp].Cell!;
        initializer.HomeObject = constructor.Get(new PropertyKey("prototype")).TryGetObject();
        constructor.FieldInitializer = initializer;
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteCallClassStaticInitializer(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        var initializer = (JsClosure)values[--sp].Cell!;
        var constructor = (JsClosure)values[--sp].Cell!;
        initializer.HomeObject = constructor;
        frame.Sp = sp;
        initializer.Call(constructor, []);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteSetHomeObject(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        var home = values[--sp];
        ((JsClosure)values[sp - 1].Cell!).HomeObject = home.TryGetObject();
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteDefineClassField(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        var key = frame.Code.Keys[code[pc++]];
        var value = values[--sp];
        var target = (JsObject)values[--sp].Cell!;
        target.CreateDataPropertyOrThrow(key, value);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteDefineClassFieldComputed(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        var value = values[--sp];
        var key = JsOperations.ToPropertyKey(values[--sp]);
        var target = (JsObject)values[--sp].Cell!;
        target.CreateDataPropertyOrThrow(key, value);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteDefinePrivateField(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        var value = values[--sp];
        var privateName = ((PrivateNameCell)values[--sp].Cell!).Name;
        var target = (JsObject)values[--sp].Cell!;
        PrivateFieldAdd(target, privateName, value);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteSetFunctionName(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        var name = values[--sp];
        SetFunctionNameAtRuntime((JsObject)values[sp - 1].Cell!, name, prefix: 0);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteSetFunctionNameComputed(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        var prefix = code[pc++];
        var name = values[--sp];
        SetFunctionNameAtRuntime((JsObject)values[sp - 1].Cell!, name, prefix);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteGetTemplateObject(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        values[sp++] = GetTemplateObject(frame.Realm, (TemplateSite)frame.Code.Objects[code[pc++]]);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteCreateRegExp(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        var pattern = frame.Code.Constants[code[pc++]].AsString();
        var flags = frame.Code.Constants[code[pc++]].AsString();
        values[sp++] = CreateRegExp(frame.Realm, pattern, flags);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteGetIterator(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        values[sp - 1] = new JsValue(new IteratorRecordCell(IteratorOperations.GetIterator(values[sp - 1])));
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteGetAsyncIterator(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        values[sp - 1] = new JsValue(new IteratorRecordCell(GetAsyncIterator(frame.Realm, values[sp - 1])));
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteIteratorNext(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        var target = code[pc++];
        var record = ((IteratorRecordCell)values[sp - 1].Cell!).Record;
        frame.Sp = sp;
        if (IteratorOperations.IteratorStepValue(record, out var value))
        {
            values[sp - 1] = value;
        }
        else
        {
            sp--;
            pc = target;
        }

        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteIteratorNextOrUndefined(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        var record = ((IteratorRecordCell)values[sp - 1].Cell!).Record;
        if (record.Done)
        {
            values[sp - 1] = default;
            frame.Pc = pc;
            return sp;
        }

        values[sp - 1] = IteratorOperations.IteratorStepValue(record, out var value) ? value : default;
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteIteratorRest(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        var record = ((IteratorRecordCell)values[sp - 1].Cell!).Record;
        var array = new JsArray(frame.Realm.Intrinsics.ArrayPrototype);
        while (!record.Done && IteratorOperations.IteratorStepValue(record, out var value))
        {
            array.Push(value);
        }

        values[sp - 1] = array;
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteIteratorCallNext(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        var record = ((IteratorRecordCell)values[sp - 1].Cell!).Record;
        values[sp - 1] = JsOperations.Call(record.NextMethod, record.Iterator, []);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteIteratorStepFinish(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        var target = code[pc++];
        var result = values[--sp];
        var record = ((IteratorRecordCell)values[sp - 1].Cell!).Record;
        if (result.Cell is not JsObject resultObject)
        {
            record.Done = true;
            throw ThrowHelper.CreateException(ErrorType.TypeError, "Iterator result is not an object");
        }

        if (IteratorOperations.IteratorComplete(resultObject))
        {
            record.Done = true;
            sp--;
            pc = target;
            frame.Pc = pc;
            return sp;
        }

        values[sp - 1] = IteratorOperations.IteratorValue(resultObject);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteIteratorClose(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        var record = ((IteratorRecordCell)values[--sp].Cell!).Record;
        frame.Sp = sp;
        if (!record.Done)
        {
            record.Done = true;
            IteratorOperations.IteratorClose(record);
        }

        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteIteratorCloseOnThrow(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        var record = ((IteratorRecordCell)values[--sp].Cell!).Record;
        frame.Sp = sp;
        if (!record.Done)
        {
            record.Done = true;
            IteratorOperations.IteratorCloseOnThrow(record);
        }

        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteIteratorCallReturn(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        var record = ((IteratorRecordCell)values[sp - 1].Cell!).Record;
        if (record.Done)
        {
            values[sp - 1] = default;
            frame.Pc = pc;
            return sp;
        }

        record.Done = true;
        var returnMethod = JsOperations.GetMethod(record.Iterator, new PropertyKey("return"));
        values[sp - 1] = returnMethod is null ? default : returnMethod.Call(record.Iterator, []);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteThrowIfNotObject(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        if (!values[sp - 1].IsObject)
        {
            ThrowHelper.ThrowTypeError("Iterator result " + JsOperations.DescribeForError(values[sp - 1]) + " is not an object");
        }

        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteIteratorIsDone(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        values[sp - 1] = JsValue.FromBoolean(((IteratorRecordCell)values[sp - 1].Cell!).Record.Done);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteIteratorDelegateStep(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        var isAsync = code[pc++] != 0;
        var kind = (int)values[--sp].NumberValue;
        var received = values[--sp];
        var record = ((IteratorRecordCell)values[sp - 1].Cell!).Record;
        frame.Sp = sp;
        values[sp - 1] = DelegateStep(record, received, kind, isAsync);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteIteratorComplete(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        values[sp - 1] = JsValue.FromBoolean(IteratorOperations.IteratorComplete(values[sp - 1].AsObject()));
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteIteratorValue(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        values[sp - 1] = IteratorOperations.IteratorValue(values[sp - 1].AsObject());
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteForInPrepare(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        values[sp - 1] = new JsValue(new ForInEnumerator(JsOperations.ToObject(values[sp - 1])));
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteForInNext(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        var target = code[pc++];
        var enumerator = (ForInEnumerator)values[sp - 1].Cell!;
        if (enumerator.MoveNext(out var key))
        {
            values[sp - 1] = key;
        }
        else
        {
            sp--;
            pc = target;
        }

        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteCreateIterResult(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        values[sp - 1] = IteratorOperations.CreateIteratorResultObject(frame.Realm, values[sp - 1], done: false);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteCreateIterResultDone(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        values[sp - 1] = IteratorOperations.CreateIteratorResultObject(frame.Realm, values[sp - 1], done: code[pc++] != 0);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteGlobalDeclarationInstantiation(Frame frame, int sp)
    {
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        GlobalDeclarationInstantiation(frame.Realm, (GlobalDeclarations)frame.Code.Objects[code[pc++]]);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteEvalDeclarationInstantiation(Frame frame, int sp)
    {
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        EvalDeclarationInstantiation(frame, (EvalDeclarations)frame.Code.Objects[code[pc++]]);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteInitializeGlobalLexical(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        InitializeGlobalLexical(frame.Realm, frame.Code.Keys[code[pc++]], values[--sp]);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteInitializeGlobalFunction(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        InitializeGlobalFunction(frame.Realm, frame.Code.Keys[code[pc++]], values[--sp]);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteSetVarBinding(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        SetVarBinding(frame, frame.Code.Keys[code[pc++]], values[--sp]);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteSetGlobalVar(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        frame.Realm.GlobalObject.Set(frame.Code.Keys[code[pc++]], values[--sp], throwOnFailure: false);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteAddDisposableResource(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        var hint = code[pc++] != 0 ? DisposeHint.AsyncDispose : DisposeHint.SyncDispose;
        var value = values[--sp];
        var capability = (DisposeCapabilityCell)values[--sp].Cell!;
        DisposableOperations.AddDisposableResource(capability.Capability, value, hint);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteNewDisposeCapability(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        values[sp++] = new JsValue(new DisposeCapabilityCell(DisposableOperations.NewDisposeCapability()));
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteDisposeResources(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        var value = values[--sp];
        var type = (int)values[--sp].NumberValue;
        var capability = ((DisposeCapabilityCell)values[--sp].Cell!).Capability;
        frame.Sp = sp;
        if (DisposableOperations.DisposeResources(capability, type == 1 ? value : (JsValue?)null) is { } thrown)
            throw new JsException(thrown);

        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteAsyncDisposeStart(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        var value = values[--sp];
        var type = (int)values[--sp].NumberValue;
        var capability = ((DisposeCapabilityCell)values[sp - 1].Cell!).Capability;
        values[sp - 1] = new JsValue(new AsyncDisposeOperationCell(new AsyncDisposeResourcesOperation(frame.Realm, capability, type == 1 ? value : (JsValue?)null)));
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteAsyncDisposeNext(Frame frame, int sp)
    {
        var values = frame.Values;
        var code = frame.Code.Code;
        var pc = frame.Pc + 1;
        var target = code[pc++];
        var operation = ((AsyncDisposeOperationCell)values[sp - 1].Cell!).Operation;
        frame.Sp = sp;
        if (operation.MoveNext(out var value))
        {
            values[sp - 1] = value;
        }
        else
        {
            sp--;
            pc = target;
        }

        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteAsyncDisposeSetResult(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        var thrown = values[--sp];
        var operation = ((AsyncDisposeOperationCell)values[--sp].Cell!).Operation;
        operation.SetAwaitResult(thrown.IsHole ? (JsValue?)null : thrown);
        frame.Pc = pc;
        return sp;
    }

    private static int ExecuteAsyncDisposeFinish(Frame frame, int sp)
    {
        var values = frame.Values;
        var pc = frame.Pc + 1;
        var operation = ((AsyncDisposeOperationCell)values[--sp].Cell!).Operation;
        if (operation.Completion is { } thrown)
            throw new JsException(thrown);

        frame.Pc = pc;
        return sp;
    }
}
