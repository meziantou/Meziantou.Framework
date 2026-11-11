using System.Globalization;
using System.Numerics;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.JavaScriptEngine.Fuzz;

/// <summary>
/// The bindings generated for <see cref="JsExportAttribute"/> turn every invalid use of a .NET class by a script into a
/// JavaScript error: a script that calls the members with any arguments and any <c>this</c>, extends the classes or
/// replaces their prototypes throws a <see cref="JsException"/> or is stopped by a limit, nothing else. The same .NET
/// object is always the same JavaScript object, and the JavaScript object of a .NET object converts back to it.
/// </summary>
public static partial class JavaScriptEngineFuzzTargets
{
    private static readonly string[] BindingTokens =
    [
        "BindingItem", "BindingBase", "BindingTools", "existing", "new BindingItem(", "BindingItem.create(", "BindingTools.sum(", "BindingTools.echo(", "BindingTools.version",
        ".name", ".count", ".ratio", ".big", ".tag", ".text", ".kind()", ".add(", ".concat(", ".child()", ".same(", ".describe(", ".items()", ".map()", ".date(", ".square(", ".laterAsync()", ".toString()", ".parent",
        ".prototype", ".constructor", ".call(", ".apply(", "Reflect.construct(", "Object.getOwnPropertyDescriptor(", "Object.setPrototypeOf(", "Object.freeze(", "extends ", "super(", "super.", "class X extends BindingItem { }",
        "undefined", "null", "NaN", "Infinity", "-0", "2 ** 53", "1.5", "1n", "'x'", "{}", "[]", "() => 1", "Symbol()", "new Date(0)", "new Proxy({}, {})", "const c = {}; c.c = c;",
    ];

    private static IEnumerable<FuzzSeed> GetBindingSeeds()
    {
        yield return FuzzSeed.FromText("binding-class", "const item = new BindingItem('a', 2); item.count += item.add(1); [item.name, item.count, `${item}`, item.child().parent === item]");
        yield return FuzzSeed.FromText("binding-members", "const item = new BindingItem(); item.text = 'b'; item.ratio = 0.5; item.big = 2 ** 53 - 1; item.tag = 1.25; [item.concat('x', null), item.describe(1, { a: [1] }), item.items(), item.map().a, item.date(new Date(0)), item.square(3n)]");
        yield return FuzzSeed.FromText("binding-errors", "const errors = []; for (const f of [() => BindingItem(), () => new BindingBase(), () => BindingItem.prototype.add.call({}), () => new BindingItem().add(1.5), () => existing.text = null, () => existing.same({}), () => BindingTools.echo(() => 1)]) { try { f(); } catch (e) { errors.push(e.name); } } errors");
        yield return FuzzSeed.FromText("binding-inheritance", "class X extends BindingItem { kind() { return 'x' + super.kind(); } } const x = new X('x'); [x.kind(), x instanceof BindingBase, x.same(x), Reflect.construct(BindingItem, [], Object).add === undefined]");
        yield return FuzzSeed.FromText("binding-static", "[BindingTools.sum(1), BindingTools.sum(1, 2), BindingTools.version, BindingTools.echo({ a: [1, 'b', null] }), BindingItem.create('c').name, Object.prototype.toString.call(BindingTools)]");
        yield return FuzzSeed.FromText("binding-promise", "existing.laterAsync().then(v => existing.count = v); existing.count");
        yield return FuzzSeed.FromText("binding-prototypes", "Object.setPrototypeOf(BindingItem.prototype, null); Object.freeze(BindingItem); const d = Object.getOwnPropertyDescriptor(BindingBase.prototype, 'kind'); d.value.call(existing)");
        yield return FuzzSeed.FromText("binding-identity", "existing.child()");
    }

    // The input is a script that uses the exported classes
    private static void Binding(ReadOnlySpan<byte> data)
    {
        var code = FuzzText.Decode(data);
        using var engine = CreateEngine(loader: null);
        var realm = engine.CreateRealm();
        var existing = new BindingItem("existing", 1);
        JsValue existingValue;
        using (engine.Enter())
        {
            realm.GlobalObject.SetProperty("BindingItem", realm.GetClass(BindingItem.JsClass));
            realm.GlobalObject.SetProperty("BindingBase", realm.GetClass(BindingBase.JsClass));
            realm.GlobalObject.SetProperty("BindingTools", realm.GetClass(BindingTools.JsClass));
            existingValue = realm.FromDotNet(existing);
            realm.GlobalObject.SetProperty("existing", existingValue);
        }

        var result = JsValue.Undefined;
        Run(() => result = realm.Execute(code, "binding.js"));
        Run(engine.RunJobs);

        using (engine.Enter())
        {
            FuzzAssert.True(JsEngine.SameValue(existingValue, realm.FromDotNet(existing)), "The .NET object is not the same JavaScript object after the script");

            // The object of the script that holds a .NET object is the JavaScript object of the .NET object
            if (result.TryGetObject() is JsHostObject { Target: BindingItem item } obj)
            {
                FuzzAssert.True(JsEngine.SameValue(obj, realm.FromDotNet(item)), "The result of the script is not the JavaScript object of its .NET object");
            }
        }

        FuzzAssert.True(ReferenceEquals(existing, engine.ToDotNet<BindingItem>(existingValue)), "The JavaScript object does not convert back to its .NET object");
    }

    [JsExport]
    internal abstract partial class BindingBase
    {
        public virtual string Kind() => "base";
    }

    [JsExport]
    internal sealed partial class BindingItem : BindingBase
    {
        public BindingItem(string? name = null, int count = 0)
        {
            Name = name;
            Count = count;
        }

        public string? Name { get; set; }

        public int Count { get; set; }

        public double Ratio { get; set; }

        public long Big { get; set; }

        public decimal? Tag { get; set; }

        public string Text { get; set; } = "";

        public BindingItem? Parent { get; private set; }

        public override string Kind() => "item";

        public int Add(int value, int step = 1) => unchecked(Count + (value * step));

        public string Concat(string first, string? second) => first + second + Name;

        public BindingItem Child() => new(Name + "/child") { Parent = this };

        public bool Same(BindingItem other) => ReferenceEquals(this, other);

        public string Describe(JsValue value, object? any) => Name + ":" + value.Kind + ":" + (any?.GetType().Name ?? "null");

        public int[] Items() => [Count, Count + 1];

        public Dictionary<string, int> Map() => new(StringComparer.Ordinal) { ["a"] = Count };

        public double Date(DateTimeOffset date) => date.ToUnixTimeMilliseconds() + Count;

        public BigInteger Square(BigInteger value) => (value * value) + Count;

        public Task<int> LaterAsync() => Task.FromResult(Count + 1);

        [JsName("toString")]
        public override string ToString() => "BindingItem(" + Name + ", " + Count.ToString(CultureInfo.InvariantCulture) + ")";

        public static BindingItem Create(string name) => new(name);
    }

    [JsExport]
    internal static partial class BindingTools
    {
        public const string Version = "1";

        public static double Sum(double a, double b = 0) => a + b;

        public static object? Echo(object? value) => value;
    }
}
