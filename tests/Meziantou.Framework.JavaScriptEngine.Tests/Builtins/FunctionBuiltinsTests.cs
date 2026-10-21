namespace Meziantou.Framework.JavaScriptEngine.Tests.Builtins;

public sealed class FunctionBuiltinsTests
{
    [Fact]
    public void FunctionPrototype()
    {
        using var context = new FundamentalsTestContext();
        var prototype = context.Intrinsics.FunctionPrototype;
        Assert.Same(context.Intrinsics.Function, context.Global("Function"));
        Assert.Same(context.Intrinsics.Function, prototype.Get("constructor").AsObject());
        Assert.Equal(0, prototype.Get("length").AsNumber());
        Assert.Equal("", prototype.Get("name").AsString());
        Assert.True(prototype.Call(JsValue.Undefined, [1]).IsUndefined);

        Assert.True(prototype.GetOwnProperty(WellKnownSymbols.HasInstance, out var hasInstance));
        Assert.Equal(PropertyFlags.None, hasInstance.Flags);
        Assert.Equal("[Symbol.hasInstance]", FundamentalsTestContext.Get(hasInstance.Value, "name").AsString());

        // caller and arguments are %ThrowTypeError% accessors
        Assert.True(prototype.GetOwnProperty("caller", out var caller));
        Assert.True(caller.Configurable);
        Assert.False(caller.Enumerable);
        Assert.Same(context.Intrinsics.ThrowTypeError, caller.Get.AsObject());
        Assert.Same(context.Intrinsics.ThrowTypeError, caller.Set.AsObject());
        context.AssertThrows(ErrorType.TypeError, () => prototype.Get("arguments"));
    }

    [Fact]
    public void Constructor()
    {
        using var context = new FundamentalsTestContext();
        var source = DynamicFunctions.GetSource(DynamicFunctionKind.Normal, ["a", "b", "return a + b"]);
        Assert.Equal("a,b", source.Parameters);
        Assert.Equal("return a + b", source.Body);
        Assert.Equal("function anonymous(a,b\n) {\nreturn a + b\n}", source.SourceText);
        Assert.Equal("async function* anonymous(\n) {\n\n}", DynamicFunctions.GetSource(DynamicFunctionKind.AsyncGenerator, []).SourceText);

        var add = context.Global("Function").Call(JsValue.Undefined, ["a", "b", "return a + b"]).AsObject();
        Assert.Equal(3, add.Call(JsValue.Undefined, [1, 2]).AsNumber());
        Assert.Equal(source.SourceText, context.CallPrototypeMethod("Function", "toString", add).AsString());
        context.AssertThrows(ErrorType.SyntaxError, () => context.Global("Function").Call(JsValue.Undefined, ["}"]));
    }

    [Fact]
    public void CallAndApply()
    {
        using var context = new FundamentalsTestContext();
        var function = context.Function((thisValue, args) => context.NewArray([thisValue, .. args]));
        var thisArg = context.NewObject();

        var result = (JsArray)FundamentalsTestContext.Invoke(function, "call", thisArg, 1, 2).AsObject();
        Assert.Equal(3u, result.Length);
        Assert.Same(thisArg, result.Get(new PropertyKey(0u)).AsObject());

        result = (JsArray)FundamentalsTestContext.Invoke(function, "apply", thisArg, context.NewArray(1, 2, 3)).AsObject();
        Assert.Equal(4u, result.Length);

        result = (JsArray)FundamentalsTestContext.Invoke(function, "apply", thisArg, JsValue.Null).AsObject();
        Assert.Equal(1u, result.Length);

        // An array-like object
        var arrayLike = context.NewObject();
        arrayLike.CreateDataPropertyOrThrow("length", 2);
        arrayLike.CreateDataPropertyOrThrow(new PropertyKey(1u), "b");
        result = (JsArray)FundamentalsTestContext.Invoke(function, "apply", thisArg, arrayLike).AsObject();
        Assert.Equal(3u, result.Length);
        Assert.True(result.Get(new PropertyKey(1u)).IsUndefined);
        Assert.Equal("b", result.Get(new PropertyKey(2u)).AsString());

        context.AssertThrows(ErrorType.TypeError, () => FundamentalsTestContext.Invoke(function, "apply", thisArg, 1));
        context.AssertThrows(ErrorType.TypeError, () => context.CallPrototypeMethod("Function", "call", context.NewObject()));
    }

    [Fact]
    public void Bind()
    {
        using var context = new FundamentalsTestContext();
        var target = context.Function((thisValue, args) => context.NewArray([thisValue, .. args]), name: "target", length: 3);
        var thisArg = context.NewObject();
        var bound = FundamentalsTestContext.Invoke(target, "bind", thisArg, 1).AsObject();

        Assert.True(bound.IsCallable);
        Assert.False(bound.IsConstructor);
        Assert.Equal("bound target", bound.Get("name").AsString());
        Assert.Equal(2, bound.Get("length").AsNumber());
        Assert.True(bound.GetOwnProperty("length", out var length));
        Assert.Equal(PropertyFlags.Configurable, length.Flags);
        Assert.Same(context.Intrinsics.FunctionPrototype, bound.GetPrototypeOf());
        Assert.Same(target, ((IBoundFunction)bound).BoundTargetFunction);

        var result = (JsArray)bound.Call(JsValue.Undefined, [2]).AsObject();
        Assert.Equal(3u, result.Length);
        Assert.Same(thisArg, result.Get(new PropertyKey(0u)).AsObject());
        Assert.Equal(1, result.Get(new PropertyKey(1u)).AsNumber());
        Assert.Equal(2, result.Get(new PropertyKey(2u)).AsNumber());

        // Bound function of a bound function
        var bound2 = FundamentalsTestContext.Invoke(bound, "bind", JsValue.Null, 5, 6, 7).AsObject();
        Assert.Equal("bound bound target", bound2.Get("name").AsString());
        Assert.Equal(0, bound2.Get("length").AsNumber());
        Assert.Same(context.Realm, ObjectOperations.GetFunctionRealm(bound2));

        context.AssertThrows(ErrorType.TypeError, () => context.CallPrototypeMethod("Function", "bind", context.NewObject()));
    }

    [Fact]
    public void Bind_LengthAndName()
    {
        using var context = new FundamentalsTestContext();
        var target = context.Function((_, _) => JsValue.Undefined);

        void SetLength(JsValue value) => target.DefineBuiltIn("length", value, PropertyFlags.Configurable);
        double BoundLength() => FundamentalsTestContext.Invoke(target, "bind", JsValue.Undefined, 1).AsObject().Get("length").AsNumber();

        SetLength(double.PositiveInfinity);
        Assert.Equal(double.PositiveInfinity, BoundLength());
        SetLength(double.NegativeInfinity);
        Assert.Equal(0, BoundLength());
        SetLength(-5);
        Assert.Equal(0, BoundLength());
        SetLength(3.7);
        Assert.Equal(2, BoundLength());
        SetLength("3");
        Assert.Equal(0, BoundLength());
        target.Delete("length");
        Assert.Equal(0, BoundLength());

        target.DefineBuiltIn("name", 1, PropertyFlags.Configurable);
        Assert.Equal("bound ", FundamentalsTestContext.Invoke(target, "bind", JsValue.Undefined).AsObject().Get("name").AsString());
    }

    [Fact]
    public void Bind_Construct()
    {
        using var context = new FundamentalsTestContext();
        JsObject? receivedNewTarget = null;
        var target = context.Function(
            (_, args, newTarget) =>
            {
                receivedNewTarget = newTarget;
                return context.NewArray(args);
            },
            isConstructor: true);
        var bound = FundamentalsTestContext.Invoke(target, "bind", JsValue.Undefined, 1).AsObject();
        Assert.True(bound.IsConstructor);

        var result = (JsArray)JsOperations.Construct(bound, [2]).AsObject();
        Assert.Equal(2u, result.Length);
        Assert.Same(target, receivedNewTarget);

        var otherNewTarget = context.Function((_, _) => JsValue.Undefined, isConstructor: true);
        JsOperations.Construct(bound, [], otherNewTarget);
        Assert.Same(otherNewTarget, receivedNewTarget);

        // instanceof uses the target
        var instance = new JsObject(context.NewObject());
        target.DefineBuiltIn("prototype", instance.GetPrototypeOf());
        Assert.True(ObjectOperations.InstanceOf(instance, bound));
    }

    [Fact]
    public void Bind_DeepChainThrowsRangeError()
    {
        using var context = new FundamentalsTestContext();
        JsObject function = context.Function((_, _) => 1);
        for (var i = 0; i < 100_000; i++)
        {
            function = JsBoundFunction.Create(context.Engine, function, JsValue.Undefined, []);
        }

        context.AssertThrows(ErrorType.RangeError, () => function.Call(JsValue.Undefined, []));
    }

    [Fact]
    public void ToString_NativeFunction()
    {
        using var context = new FundamentalsTestContext();
        string Source(JsValue value) => context.CallPrototypeMethod("Function", "toString", value).AsString();

        Assert.Equal("function Object() { [native code] }", Source(context.Global("Object")));
        Assert.Equal("function () { [native code] }", Source(context.Intrinsics.FunctionPrototype));
        Assert.Equal("function [Symbol.hasInstance]() { [native code] }", Source(context.Intrinsics.FunctionPrototype.Get(WellKnownSymbols.HasInstance)));

        context.Intrinsics.ObjectPrototype.GetOwnProperty("__proto__", out var proto);
        Assert.Equal("function get __proto__() { [native code] }", Source(proto.Get));

        // The name property may change: toString uses the initial name
        var function = context.Function((_, _) => 1, name: "initial");
        function.DefineBuiltIn("name", "changed", PropertyFlags.Configurable);
        Assert.Equal("function initial() { [native code] }", Source(function));

        Assert.Equal("function () { [native code] }", Source(FundamentalsTestContext.Invoke(function, "bind", JsValue.Undefined)));
        Assert.Equal("function () { [native code] }", Source(context.NewProxy(function)));
        Assert.Equal("function () { [native code] }", Source(context.Function((_, _) => 1, name: "not valid")));
        context.AssertThrows(ErrorType.TypeError, () => Source(context.NewObject()));
        context.AssertThrows(ErrorType.TypeError, () => Source(context.NewProxy(context.NewObject())));
    }

    [Fact]
    public void HasInstance()
    {
        using var context = new FundamentalsTestContext();
        var constructor = context.Function((_, _) => JsValue.Undefined, isConstructor: true);
        var prototype = context.NewObject();
        constructor.DefineBuiltIn("prototype", prototype);
        var hasInstance = context.Intrinsics.FunctionPrototype.Get(WellKnownSymbols.HasInstance);
        Assert.True(JsOperations.Call(hasInstance, constructor, [new JsObject(prototype)]).AsBoolean());
        Assert.False(JsOperations.Call(hasInstance, constructor, [context.NewObject()]).AsBoolean());
        Assert.False(JsOperations.Call(hasInstance, context.NewObject(), [new JsObject(prototype)]).AsBoolean());
    }
}
