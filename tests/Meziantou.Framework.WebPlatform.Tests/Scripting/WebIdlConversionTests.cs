using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Scripting.Generated;

namespace Meziantou.Framework.WebPlatform.Tests.Scripting;

/// <summary>
/// The WebIDL conversions of the shared bindings (https://webidl.spec.whatwg.org/#es-type-mapping), tested with an
/// in-memory adapter: they only use the primitives of <see cref="IJsAdapter{TValue}"/>, whatever the engine.
/// </summary>
public sealed class WebIdlConversionTests : IDisposable
{
    private readonly BrowsingContext _context = new(new BrowsingContextOptions());
    private readonly InMemoryRealm _engineRealm = new();
    private readonly RecordingHost _host = new();
    private readonly WebIdlRealm<InMemoryJsAdapter, InMemoryValue> _realm;

    public WebIdlConversionTests()
    {
        var window = _context.Window;
        var agent = new WebIdlAgent<InMemoryJsAdapter, InMemoryValue>(new InMemoryJsAdapter(_engineRealm), _host);
        _realm = new WebIdlRealm<InMemoryJsAdapter, InMemoryValue>(agent, window, _engineRealm);
    }

    public void Dispose() => _context.Dispose();

    private InMemoryJsAdapter Adapter => _realm.Adapter;

    private InMemoryValue Number(double value) => Adapter.FromNumber(value);

    private InMemoryValue String(string value) => Adapter.FromString(value);

    private InMemoryValue Object(params (string Name, InMemoryValue Value)[] properties)
    {
        var result = Adapter.CreateObject(_engineRealm.ObjectPrototype);
        foreach (var (name, value) in properties)
        {
            Adapter.Set(result, String(name), value);
        }

        return result;
    }

    private string ThrowsJsError(Action action)
    {
        var exception = Assert.Throws<InMemoryJsException>(action);
        return Adapter.ToString(Adapter.Get(exception.Value, String("name")));
    }

    [Theory]
    [InlineData(3.7, 3)]
    [InlineData(-3.7, -3)]
    [InlineData(2147483648, -2147483648)]
    [InlineData(4294967297, 1)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    public void Long_IsConvertedModulo2Pow32(double value, int expected)
        => Assert.Equal(expected, _realm.ToLong(Number(value)));

    [Theory]
    [InlineData(-1, 4294967295u)]
    [InlineData(4294967296, 0u)]
    public void UnsignedLong_IsConvertedModulo2Pow32(double value, uint expected)
        => Assert.Equal(expected, _realm.ToUnsignedLong(Number(value)));

    [Theory]
    [InlineData(256, 0)]
    [InlineData(-1, 255)]
    [InlineData(511.9, 255)]
    public void Octet_IsConvertedModulo2Pow8(double value, byte expected)
        => Assert.Equal(expected, _realm.ToOctet(Number(value)));

    [Theory]
    [InlineData(300, 255)]
    [InlineData(-5, 0)]
    [InlineData(2.5, 2)]
    [InlineData(3.5, 4)]
    [InlineData(double.NaN, 0)]
    public void Clamp_RoundsHalfToEvenAndClampsToTheRange(double value, byte expected)
        => Assert.Equal(expected, _realm.ToOctet(Number(value), IntegerConversion.Clamp));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(256)]
    [InlineData(-1)]
    public void EnforceRange_ThrowsATypeErrorOutsideOfTheRange(double value)
        => Assert.Equal("TypeError", ThrowsJsError(() => _realm.ToOctet(Number(value), IntegerConversion.EnforceRange)));

    [Fact]
    public void EnforceRange_TruncatesTheValue()
        => Assert.Equal(255, _realm.ToOctet(Number(255.9), IntegerConversion.EnforceRange));

    [Fact]
    public void LongLong_UsesTheSafeIntegerRange()
    {
        Assert.Equal(9007199254740991L, _realm.ToLongLong(Number(9007199254740991), IntegerConversion.EnforceRange));
        Assert.Equal("TypeError", ThrowsJsError(() => _realm.ToLongLong(Number(9007199254740992), IntegerConversion.EnforceRange)));
    }

    [Fact]
    public void Integer_ConvertsStringsAndBooleans()
    {
        Assert.Equal(42, _realm.ToLong(String(" 42 ")));
        Assert.Equal(1, _realm.ToLong(Adapter.FromBoolean(true)));
        Assert.Equal(0, _realm.ToLong(Adapter.Null));
        Assert.Equal(0, _realm.ToLong(Adapter.Undefined));
    }

    [Fact]
    public void Integer_ThrowsTheExceptionOfValueOf()
    {
        var obj = Object(("valueOf", Adapter.CreateFunction("valueOf", 0, (_, _) => throw _engineRealm.Throw("RangeError", "valueOf"))));
        Assert.Equal("RangeError", ThrowsJsError(() => _realm.ToLong(obj)));
    }

    [Fact]
    public void Integer_UsesValueOf()
    {
        var obj = Object(("valueOf", Adapter.CreateFunction("valueOf", 0, (_, _) => Number(12))));
        Assert.Equal(12, _realm.ToLong(obj));
    }

    [Fact]
    public void RestrictedDouble_ThrowsATypeErrorForNonFiniteValues()
    {
        Assert.Equal(1.5, _realm.ToDouble(Number(1.5), restricted: true));
        Assert.Equal("TypeError", ThrowsJsError(() => _realm.ToDouble(Number(double.NaN), restricted: true)));
        Assert.Equal("TypeError", ThrowsJsError(() => _realm.ToDouble(String("Infinity"), restricted: true)));
        Assert.True(double.IsPositiveInfinity(_realm.ToDouble(String("Infinity"), restricted: false)));
    }

    [Fact]
    public void RestrictedFloat_ThrowsATypeErrorWhenTheValueOverflows()
    {
        Assert.Equal(0.1f, _realm.ToFloat(Number(0.1), restricted: true));
        Assert.Equal("TypeError", ThrowsJsError(() => _realm.ToFloat(Number(1e300), restricted: true)));
        Assert.True(float.IsPositiveInfinity(_realm.ToFloat(Number(1e300), restricted: false)));
    }

    [Fact]
    public void DomString_UsesToString()
    {
        Assert.Equal("null", _realm.ToDomString(Adapter.Null));
        Assert.Equal("undefined", _realm.ToDomString(Adapter.Undefined));
        Assert.Equal("1.5", _realm.ToDomString(Number(1.5)));
        Assert.Equal("true", _realm.ToDomString(Adapter.FromBoolean(true)));
        Assert.Equal("TypeError", ThrowsJsError(() => _realm.ToDomString(Adapter.GetWellKnownSymbol(JsWellKnownSymbol.Iterator))));
    }

    [Fact]
    public void DomString_LegacyNullToEmptyString()
    {
        Assert.Equal("", _realm.ToDomString(Adapter.Null, legacyNullToEmptyString: true));
        Assert.Equal("undefined", _realm.ToDomString(Adapter.Undefined, legacyNullToEmptyString: true));
    }

    [Fact]
    public void UsvString_ReplacesTheLoneSurrogates()
    {
        var loneHighSurrogate = new string((char)0xD800, 1);
        var loneLowSurrogate = new string((char)0xDC00, 1);
        var pair = char.ConvertFromUtf32(0x1F600);
        var replacement = new string((char)0xFFFD, 1);
        Assert.Equal("a" + replacement + "b" + replacement + pair, _realm.ToUsvString(String("a" + loneHighSurrogate + "b" + loneLowSurrogate + pair)));
    }

    [Fact]
    public void ByteString_ThrowsATypeErrorForCharactersAbove255()
    {
        Assert.Equal("caf" + (char)0xE9, _realm.ToByteString(String("caf" + (char)0xE9)));
        Assert.Equal("TypeError", ThrowsJsError(() => _realm.ToByteString(String("a" + (char)0x100))));
    }

    [Fact]
    public void Enumeration_ThrowsATypeErrorForAnUnknownValue()
    {
        string[] values = ["auto", "manual"];
        Assert.Equal("manual", _realm.ToEnumerationValue(String("manual"), "ScrollRestoration", values));
        Assert.Equal("TypeError", ThrowsJsError(() => _realm.ToEnumerationValue(String("Manual"), "ScrollRestoration", values)));
        Assert.Equal(ScrollRestoration.Manual, WebIdlHelpers<InMemoryJsAdapter, InMemoryValue>.ToEnum_Dom_ScrollRestoration("manual"));
        Assert.Equal("auto", WebIdlHelpers<InMemoryJsAdapter, InMemoryValue>.FromEnum_Dom_ScrollRestoration(ScrollRestoration.Auto));
    }

    [Fact]
    public void Sequence_IteratesTheValue()
    {
        var array = Adapter.CreateArray([Number(1), String("2"), Adapter.FromBoolean(true)]);
        Assert.Equal([1, 2, 1], _realm.ToSequence(array, static (r, e) => r.ToLong(e)));
    }

    [Fact]
    public void Sequence_ThrowsATypeErrorForANonIterableValue()
    {
        Assert.Equal("TypeError", ThrowsJsError(() => _realm.ToSequence(Number(1), static (r, e) => r.ToLong(e))));
        Assert.Equal("TypeError", ThrowsJsError(() => _realm.ToSequence(Object(), static (r, e) => r.ToLong(e))));
    }

    [Fact]
    public void Record_ConvertsTheEnumerableOwnProperties()
    {
        var obj = Object(("a", Number(1)), ("b", String("2")));
        var result = _realm.ToRecord(obj, static (r, k) => r.ToDomString(k), static (r, v) => r.ToLong(v));
        Assert.Equal([new("a", 1), new("b", 2)], result);
    }

    [Fact]
    public void Dictionary_UsesTheDefaultValuesOfTheMissingMembers()
    {
        var init = WebIdlHelpers<InMemoryJsAdapter, InMemoryValue>.ToDictionary_Dom_EventInit(_realm, Object(("bubbles", Number(1))));
        Assert.True(init.Bubbles);
        Assert.False(init.Cancelable);
        Assert.False(init.Composed);

        var fromUndefined = WebIdlHelpers<InMemoryJsAdapter, InMemoryValue>.ToDictionary_Dom_EventInit(_realm, Adapter.Undefined);
        Assert.False(fromUndefined.Bubbles);
    }

    [Fact]
    public void Dictionary_DoesNotSetTheMissingMembersWhosePresenceIsTracked()
    {
        var options = WebIdlHelpers<InMemoryJsAdapter, InMemoryValue>.ToDictionary_Dom_ElementCreationOptions(_realm, Object(("is", String("x-y"))));
        Assert.Equal("x-y", options.Is);
        Assert.False(options.HasCustomElementRegistry);

        options = WebIdlHelpers<InMemoryJsAdapter, InMemoryValue>.ToDictionary_Dom_ElementCreationOptions(_realm, Object(("customElementRegistry", Adapter.Null)));
        Assert.True(options.HasCustomElementRegistry);
        Assert.Null(options.CustomElementRegistry);
    }

    [Fact]
    public void Dictionary_ThrowsATypeErrorForAPrimitive()
        => Assert.Equal("TypeError", ThrowsJsError(() => WebIdlHelpers<InMemoryJsAdapter, InMemoryValue>.ToDictionary_Dom_EventInit(_realm, Number(1))));

    [Fact]
    public void Dictionary_IsConvertedToAnObject()
    {
        var value = WebIdlHelpers<InMemoryJsAdapter, InMemoryValue>.FromDictionary_Dom_EventInit(_realm, new EventInit { Bubbles = true });
        Assert.True(Adapter.ToBoolean(Adapter.Get(value, String("bubbles"))));
        Assert.False(Adapter.ToBoolean(Adapter.Get(value, String("cancelable"))));
    }

    [Fact]
    public void CallbackFunction_ThrowsATypeErrorForANonCallableValue()
    {
        Assert.Equal("TypeError", ThrowsJsError(() => _realm.ToCallbackFunction(Object(), "Function", treatNonObjectAsNull: false)));
        Assert.Null(_realm.ToCallbackFunction(Number(1), "EventHandlerNonNull", treatNonObjectAsNull: true));
    }

    [Fact]
    public void CallbackFunction_IsConvertedToADelegateThatReportsTheExceptions()
    {
        var calls = 0;
        var function = Adapter.CreateFunction("f", 0, (_, _) =>
        {
            calls++;
            throw _engineRealm.Throw("TypeError", "boom");
        });

        var action = WebIdlHelpers<InMemoryJsAdapter, InMemoryValue>.ToDelegate_System_Action(_realm, function);
        action();

        Assert.Equal(1, calls);
        Assert.Equal(["Invoking", "ReportException", "Invoked"], _host.Calls);
        Assert.Same(action, WebIdlHelpers<InMemoryJsAdapter, InMemoryValue>.ToDelegate_System_Action(_realm, function));
    }

    [Fact]
    public void ClrValues_AreConvertedByTheirRuntimeType()
    {
        Assert.Equal("a", Adapter.AsString(_realm.ToJs("a")));
        Assert.Equal(1.5, Adapter.AsNumber(_realm.ToJs(1.5)));
        Assert.Equal(3, Adapter.AsNumber(_realm.ToJs(3)));
        Assert.True(Adapter.AsBoolean(_realm.ToJs(true)));
        Assert.True(Adapter.IsNull(_realm.ToJs(null)));
        Assert.Equal("manual", Adapter.AsString(_realm.ToJs(ScrollRestoration.Manual)));
    }

    private sealed class RecordingHost : IWebIdlHost
    {
        public List<string> Calls { get; } = [];

        public void ReportException(GlobalScope global, Exception exception) => Calls.Add("ReportException");

        public void OnCallbackInvoking() => Calls.Add("Invoking");

        public void OnCallbackInvoked(GlobalScope global) => Calls.Add("Invoked");
    }
}
