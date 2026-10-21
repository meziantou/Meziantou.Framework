namespace Meziantou.Framework.JavaScriptEngine.Tests.Builtins;

public sealed class RegExpBuiltinsTests
{
    /// <summary>An engine, a realm, and the engine entered on the current thread.</summary>
    private sealed class Context : IDisposable
    {
        private readonly JsEngine _engine;
        private readonly JsEngineScope _scope;

        public Context()
        {
            _engine = new JsEngine();
            Realm = _engine.CreateRealm();
            _scope = _engine.Enter();
            _engine.CurrentRealm = Realm;
        }

        public JsRealm Realm { get; }

        public JsObject RegExp => Global("RegExp").AsObject();

        public JsValue Global(string name) => Realm.GlobalObject.Get(new PropertyKey(name));

        public JsObject NewRegExp(string pattern, string flags = "") => RegExp.Construct([pattern, flags], RegExp).AsObject();

        public static JsValue Invoke(JsValue target, PropertyKey name, params JsValue[] arguments) => JsOperations.Invoke(target, name, arguments);

        public JsValue StringMethod(string value, string name, params JsValue[] arguments) => Invoke(value, name, arguments);

        public void Dispose()
        {
            _scope.Dispose();
            _engine.Dispose();
        }
    }

    private static string ErrorName(Action action)
    {
        var exception = Assert.Throws<JsException>(action);
        return exception.Value.AsObject().Get(new PropertyKey("name")).AsString();
    }

    private static string?[] ToStrings(JsValue array)
    {
        var obj = array.AsObject();
        var length = (int)JsOperations.LengthOfArrayLike(obj);
        var result = new string?[length];
        for (var i = 0; i < length; i++)
        {
            var value = obj.Get(new PropertyKey((uint)i));
            result[i] = value.IsUndefined ? null : JsOperations.ToString(value);
        }

        return result;
    }

    private static string Pair(JsValue pair) => string.Join(",", ToStrings(pair));

    [Fact]
    public void Constructor()
    {
        using var context = new Context();
        var regexp = context.NewRegExp("a+", "gi");
        Assert.Equal("a+", regexp.Get(new PropertyKey("source")).AsString());
        Assert.Equal("gi", regexp.Get(new PropertyKey("flags")).AsString());
        Assert.Equal(0, regexp.Get(new PropertyKey("lastIndex")).AsNumber());
        Assert.True(regexp.Get(new PropertyKey("global")).AsBoolean());
        Assert.False(regexp.Get(new PropertyKey("sticky")).AsBoolean());

        // RegExp(re) returns re; new RegExp(re) creates a copy, with other flags if any
        Assert.Same(regexp, context.RegExp.Call(JsValue.Undefined, [regexp]).AsObject());
        var copy = context.RegExp.Construct([regexp], context.RegExp).AsObject();
        Assert.NotSame(regexp, copy);
        Assert.Equal("gi", copy.Get(new PropertyKey("flags")).AsString());
        var other = context.RegExp.Construct([regexp, "y"], context.RegExp).AsObject();
        Assert.Equal("y", other.Get(new PropertyKey("flags")).AsString());
        Assert.Equal("(?:)", context.RegExp.Construct([], context.RegExp).AsObject().Get(new PropertyKey("source")).AsString());

        Assert.Equal("SyntaxError", ErrorName(() => context.NewRegExp("a", "gg")));
        Assert.Equal("SyntaxError", ErrorName(() => context.NewRegExp("a", "uv")));
        Assert.Equal("SyntaxError", ErrorName(() => context.NewRegExp("(")));
        Assert.Equal("SyntaxError", ErrorName(() => context.NewRegExp("\\p{Foo}", "u")));
    }

    [Fact]
    public void Properties()
    {
        using var context = new Context();
        var regexp = context.RegExp;
        Assert.Equal(2, regexp.Get(new PropertyKey("length")).AsNumber());
        Assert.Equal("RegExp", regexp.Get(new PropertyKey("name")).AsString());
        Assert.Same(regexp, regexp.Get(new PropertyKey(WellKnownSymbols.Species)).AsObject());
        var prototype = regexp.Get(new PropertyKey("prototype")).AsObject();
        Assert.True(prototype.GetOwnProperty(new PropertyKey("prototype"), out _) is false);
        Assert.True(regexp.GetOwnProperty(new PropertyKey("prototype"), out var descriptor));
        Assert.False(descriptor.Writable || descriptor.Configurable || descriptor.Enumerable);

        // The getters return undefined for %RegExp.prototype% itself
        Assert.True(prototype.Get(new PropertyKey("global")).IsUndefined);
        Assert.Equal("(?:)", prototype.Get(new PropertyKey("source")).AsString());
        Assert.Equal("", prototype.Get(new PropertyKey("flags")).AsString());
        Assert.Equal("/(?:)/", JsOperations.ToString(Context.Invoke(prototype, "toString")));

        Assert.True(prototype.GetOwnProperty(new PropertyKey(WellKnownSymbols.Split), out descriptor));
        Assert.Equal("[Symbol.split]", descriptor.Value.AsObject().Get(new PropertyKey("name")).AsString());
        Assert.Equal(2, descriptor.Value.AsObject().Get(new PropertyKey("length")).AsNumber());
        Assert.True(prototype.GetOwnProperty(new PropertyKey("flags"), out descriptor));
        Assert.Equal("get flags", descriptor.Get.AsObject().Get(new PropertyKey("name")).AsString());
        Assert.True(regexp.GetOwnProperty(new PropertyKey("$1"), out descriptor));
        Assert.Equal("get $1", descriptor.Get.AsObject().Get(new PropertyKey("name")).AsString());
        Assert.True(descriptor.Set.IsUndefined);
        Assert.True(regexp.GetOwnProperty(new PropertyKey("input"), out descriptor));
        Assert.Equal("set input", descriptor.Set.AsObject().Get(new PropertyKey("name")).AsString());

        var regexpObject = context.NewRegExp("a");
        Assert.True(regexpObject.GetOwnProperty(new PropertyKey("lastIndex"), out descriptor));
        Assert.True(descriptor.Writable);
        Assert.False(descriptor.Enumerable || descriptor.Configurable);
    }

    [Fact]
    public void Flags()
    {
        using var context = new Context();
        Assert.Equal("dgimsvy", context.NewRegExp("a", "ysvmigd").Get(new PropertyKey("flags")).AsString());
        Assert.Equal("u", context.NewRegExp("a", "u").Get(new PropertyKey("flags")).AsString());
        Assert.True(context.NewRegExp("a", "v").Get(new PropertyKey("unicodeSets")).AsBoolean());
        Assert.True(context.NewRegExp("a", "d").Get(new PropertyKey("hasIndices")).AsBoolean());
        Assert.True(context.NewRegExp("a", "s").Get(new PropertyKey("dotAll")).AsBoolean());
    }

    [Theory]
    [InlineData("/", "", "\\/")]
    [InlineData("[/]", "", "[/]")]
    [InlineData("\\/", "", "\\/")]
    [InlineData("a\nb", "", "a\\nb")]
    [InlineData("\r\u2028\u2029", "", "\\r\\u2028\\u2029")]
    [InlineData("[[a]]/", "v", "[[a]]\\/")]
    [InlineData("[[/]/", "", "[[/]\\/")]
    [InlineData("", "", "(?:)")]
    public void Source(string pattern, string flags, string expected)
    {
        using var context = new Context();
        Assert.Equal(expected, context.NewRegExp(pattern, flags).Get(new PropertyKey("source")).AsString());
    }

    [Fact]
    public void Exec()
    {
        using var context = new Context();
        var regexp = context.NewRegExp("(?<year>\\d{4})-(?<month>\\d{2})(x)?", "d");
        var result = Context.Invoke(regexp, "exec", "on 2024-05!").AsObject();
        Assert.Equal(["2024-05", "2024", "05", null], ToStrings(result));
        Assert.Equal(3, result.Get(new PropertyKey("index")).AsNumber());
        Assert.Equal("on 2024-05!", result.Get(new PropertyKey("input")).AsString());
        var groups = result.Get(new PropertyKey("groups")).AsObject();
        Assert.Null(groups.PrototypeSlot);
        Assert.Equal("2024", groups.Get(new PropertyKey("year")).AsString());
        Assert.Equal(["year", "month"], groups.OwnPropertyKeys().Select(key => key.ToString()));

        var indices = result.Get(new PropertyKey("indices")).AsObject();
        Assert.Equal("3,10", Pair(indices.Get(new PropertyKey(0u))));
        Assert.Equal("8,10", Pair(indices.Get(new PropertyKey(2u))));
        Assert.True(indices.Get(new PropertyKey(3u)).IsUndefined);
        Assert.Equal("3,7", Pair(indices.Get(new PropertyKey("groups")).AsObject().Get(new PropertyKey("year"))));

        Assert.Equal(["0", "1", "2", "3", "length", "index", "input", "groups", "indices"], result.OwnPropertyKeys().Select(key => key.ToString()));

        Assert.True(Context.Invoke(context.NewRegExp("x"), "exec", "abc").IsNull);
        Assert.True(Context.Invoke(context.NewRegExp("a"), "exec", "abc").AsObject().Get(new PropertyKey("groups")).IsUndefined);
        Assert.Equal("TypeError", ErrorName(() => JsOperations.Call(context.RegExp.Get(new PropertyKey("prototype")).AsObject().Get(new PropertyKey("exec")), JsValue.Null, ["a"])));
    }

    [Fact]
    public void Exec_DuplicateNamedGroups()
    {
        using var context = new Context();
        var regexp = context.NewRegExp("(?<a>x)|(?<a>y)", "d");
        var result = Context.Invoke(regexp, "exec", "y").AsObject();
        Assert.Equal(["y", null, "y"], ToStrings(result));
        Assert.Equal("y", result.Get(new PropertyKey("groups")).AsObject().Get(new PropertyKey("a")).AsString());
        Assert.Equal("0,1", Pair(result.Get(new PropertyKey("indices")).AsObject().Get(new PropertyKey("groups")).AsObject().Get(new PropertyKey("a"))));

        result = Context.Invoke(regexp, "exec", "x").AsObject();
        Assert.Equal("x", result.Get(new PropertyKey("groups")).AsObject().Get(new PropertyKey("a")).AsString());
    }

    [Fact]
    public void Exec_LastIndex()
    {
        using var context = new Context();
        var regexp = context.NewRegExp("a", "g");
        var lastIndex = new PropertyKey("lastIndex");
        Assert.Equal(0, Context.Invoke(regexp, "exec", "aba").AsObject().Get(new PropertyKey("index")).AsNumber());
        Assert.Equal(1, regexp.Get(lastIndex).AsNumber());
        Assert.Equal(2, Context.Invoke(regexp, "exec", "aba").AsObject().Get(new PropertyKey("index")).AsNumber());
        Assert.Equal(3, regexp.Get(lastIndex).AsNumber());
        Assert.True(Context.Invoke(regexp, "exec", "aba").IsNull);
        Assert.Equal(0, regexp.Get(lastIndex).AsNumber());

        var sticky = context.NewRegExp("a", "y");
        Assert.True(Context.Invoke(sticky, "test", "ba").AsBoolean() is false);
        Assert.Equal(0, sticky.Get(lastIndex).AsNumber());
        sticky.Set(lastIndex, 1, throwOnFailure: true);
        Assert.True(Context.Invoke(sticky, "test", "ba").AsBoolean());
        Assert.Equal(2, sticky.Get(lastIndex).AsNumber());

        // Without g and y, lastIndex is ignored and not written
        var plain = context.NewRegExp("a");
        plain.Set(lastIndex, 5, throwOnFailure: true);
        Assert.True(Context.Invoke(plain, "test", "a").AsBoolean());
        Assert.Equal(5, plain.Get(lastIndex).AsNumber());

        var unicode = context.NewRegExp(".", "gu");
        Context.Invoke(unicode, "exec", "\U0001D306");
        Assert.Equal(2, unicode.Get(lastIndex).AsNumber());
    }

    [Fact]
    public void ToStringAndTest()
    {
        using var context = new Context();
        Assert.Equal("/a\\/b/gi", JsOperations.ToString(Context.Invoke(context.NewRegExp("a/b", "ig"), "toString")));
        Assert.True(Context.Invoke(context.NewRegExp("b"), "test", "abc").AsBoolean());
        Assert.False(Context.Invoke(context.NewRegExp("d"), "test", "abc").AsBoolean());
    }

    [Theory]
    [InlineData("a.b", "\\x61\\.b")]
    [InlineData("foo bar", "\\x66oo\\x20bar")]
    [InlineData("1+1", "\\x31\\+1")]
    [InlineData("_a", "_a")]
    [InlineData("\n", "\\n")]
    [InlineData(",-=<>#&!%:;@~'`\"", "\\x2c\\x2d\\x3d\\x3c\\x3e\\x23\\x26\\x21\\x25\\x3a\\x3b\\x40\\x7e\\x27\\x60\\x22")]
    [InlineData("\u2028\uFEFF\u00A0\u3000", "\\u2028\\ufeff\\xa0\\u3000")]
    [InlineData("\U0001F600", "\U0001F600")]
    [InlineData("^$\\.*+?()[]{}|/", "\\^\\$\\\\\\.\\*\\+\\?\\(\\)\\[\\]\\{\\}\\|\\/")]
    public void Escape(string value, string expected)
    {
        using var context = new Context();
        Assert.Equal(expected, Context.Invoke(context.RegExp, "escape", value).AsString());
    }

    [Fact]
    public void Escape_LoneSurrogate()
    {
        using var context = new Context();
        Assert.Equal("\\ud83d", Context.Invoke(context.RegExp, "escape", "\uD83D").AsString());
        Assert.Equal("TypeError", ErrorName(() => Context.Invoke(context.RegExp, "escape", 1)));
    }

    [Theory]
    [InlineData("aXbX", "X", "g", "-", "a-b-")]
    [InlineData("aXbX", "X", "", "-", "a-bX")]
    [InlineData("abc", "b", "", "[$&]", "a[b]c")]
    [InlineData("abc", "b", "", "[$`]", "a[a]c")]
    [InlineData("abc", "b", "", "[$']", "a[c]c")]
    [InlineData("abc", "b", "", "$$", "a$c")]
    [InlineData("abc", "(b)", "", "[$1]", "a[b]c")]
    [InlineData("abc", "(b)", "", "[$01]", "a[b]c")]
    [InlineData("abc", "(b)", "", "[$10]", "a[b0]c")]
    [InlineData("abc", "(b)", "", "[$2]", "a[$2]c")]
    [InlineData("abc", "(b)", "", "[$0]", "a[$0]c")]
    [InlineData("abc", "(b)(x)?", "", "[$2]", "a[]c")]
    [InlineData("abc", "(?<n>b)", "", "[$<n>]", "a[b]c")]
    [InlineData("abc", "(?<n>b)", "", "[$<x>]", "a[]c")]
    [InlineData("abc", "(b)", "", "[$<n>]", "a[$<n>]c")]
    [InlineData("abc", "(?<n>b)", "", "[$<n]", "a[$<n]c")]
    [InlineData("abc", "", "g", "-", "-a-b-c-")]
    [InlineData("\U0001F600", "", "gu", "-", "-\U0001F600-")]
    [InlineData("aaa", "a*?", "g", "-", "-a-a-a-")]
    public void Replace(string input, string pattern, string flags, string replacement, string expected)
    {
        using var context = new Context();
        Assert.Equal(expected, context.StringMethod(input, "replace", context.NewRegExp(pattern, flags), replacement).AsString());
    }

    [Fact]
    public void Replace_Function()
    {
        using var context = new Context();
        var arguments = new List<string>();
        var replacer = context.Realm.CreateBuiltinFunction("replacer", 0, (_, _, args, _) =>
        {
            arguments.Add(string.Join("|", args.ToArray().Select(a => a.IsUndefined ? "undefined" : a.IsObject ? "object" : JsOperations.ToString(a))));
            return "<" + JsOperations.ToString(args[0]) + ">";
        });

        Assert.Equal("a<b>c<b>", context.StringMethod("abcb", "replace", context.NewRegExp("(b)(x)?", "g"), replacer).AsString());
        Assert.Equal(["b|b|undefined|1|abcb", "b|b|undefined|3|abcb"], arguments);

        arguments.Clear();
        Assert.Equal("a<b>c", context.StringMethod("abc", "replace", context.NewRegExp("(?<n>b)"), replacer).AsString());
        Assert.Equal(["b|b|1|abc|object"], arguments);

        arguments.Clear();
        Assert.Equal("a<b>cb", context.StringMethod("abcb", "replace", "b", replacer).AsString());
        Assert.Equal(["b|1|abcb"], arguments);
    }

    [Theory]
    [InlineData("a,b,,c", ",", "", new[] { "a", "b", "", "c" })]
    [InlineData("a1b2c", "(\\d)", "", new[] { "a", "1", "b", "2", "c" })]
    [InlineData("abc", "", "", new[] { "a", "b", "c" })]
    [InlineData("", "", "", new string[0])]
    [InlineData("", "x", "", new[] { "" })]
    [InlineData("\U0001F600\U0001F600", "", "u", new[] { "\U0001F600", "\U0001F600" })]
    [InlineData("ab", "a*?", "", new[] { "a", "b" })]
    [InlineData("ab", "a*", "", new[] { "", "b" })]
    [InlineData("A<B>bold</B>and<CODE>coded</CODE>", "<(\\/)?([^<>]+)>", "", new[] { "A", null, "B", "bold", "/", "B", "and", null, "CODE", "coded", "/", "CODE", "" })]
    public void Split(string input, string pattern, string flags, string?[] expected)
    {
        using var context = new Context();
        Assert.Equal(expected, ToStrings(context.StringMethod(input, "split", context.NewRegExp(pattern, flags))));
    }

    [Fact]
    public void Split_Limit()
    {
        using var context = new Context();
        Assert.Equal(["a", "b"], ToStrings(context.StringMethod("a,b,c", "split", context.NewRegExp(","), 2)));
        Assert.Equal(["a", ","], ToStrings(context.StringMethod("a,b,c", "split", context.NewRegExp("(,)"), 2)));
        Assert.Equal([], ToStrings(context.StringMethod("a,b,c", "split", context.NewRegExp(","), 0)));
        Assert.Equal(["a", "b", "c"], ToStrings(context.StringMethod("a,b,c", "split", ",")));
        Assert.Equal(["a", "b"], ToStrings(context.StringMethod("abc", "split", "", 2)));
        Assert.Equal(["abc"], ToStrings(context.StringMethod("abc", "split")));
        Assert.Equal([""], ToStrings(context.StringMethod("", "split", ",")));

        // The legacy static properties follow the matches of the splitter
        Assert.Equal(["a", "1", "b", "2", "c"], ToStrings(context.StringMethod("a1b2c", "split", context.NewRegExp("(\\d)"))));
        Assert.Equal("2", context.RegExp.Get(new PropertyKey("$1")).AsString());
        Assert.Equal("a1b", context.RegExp.Get(new PropertyKey("leftContext")).AsString());
    }

    [Fact]
    public void Match()
    {
        using var context = new Context();
        Assert.Equal(["a", "a", "a"], ToStrings(context.StringMethod("aba a", "match", context.NewRegExp("a", "g"))));
        Assert.True(context.StringMethod("bcd", "match", context.NewRegExp("a", "g")).IsNull);
        Assert.Equal(["", "", ""], ToStrings(context.StringMethod("ab", "match", context.NewRegExp("x*", "g"))));
        var result = context.StringMethod("xab", "match", context.NewRegExp("(a)b")).AsObject();
        Assert.Equal(["ab", "a"], ToStrings(result));
        Assert.Equal(1, result.Get(new PropertyKey("index")).AsNumber());

        // A string is a pattern
        Assert.Equal(["b"], ToStrings(context.StringMethod("abc", "match", "b")));
        Assert.Equal(["true"], ToStrings(context.StringMethod("atruebtruec", "match", JsValue.True)));
        Assert.Equal(1, context.StringMethod("abc", "search", "b").AsNumber());
        Assert.Equal(-1, context.StringMethod("abc", "search", context.NewRegExp("d")).AsNumber());
    }

    [Fact]
    public void Search_RestoresLastIndex()
    {
        using var context = new Context();
        var regexp = context.NewRegExp("b", "g");
        regexp.Set(new PropertyKey("lastIndex"), 2, throwOnFailure: true);
        Assert.Equal(1, context.StringMethod("abc", "search", regexp).AsNumber());
        Assert.Equal(2, regexp.Get(new PropertyKey("lastIndex")).AsNumber());
    }

    [Fact]
    public void MatchAll()
    {
        using var context = new Context();
        var iterator = context.StringMethod("a1b22", "matchAll", context.NewRegExp("\\d+", "g")).AsObject();
        Assert.Equal("RegExp String Iterator", iterator.Get(new PropertyKey(WellKnownSymbols.ToStringTag)).AsString());
        var values = new List<string>();
        while (true)
        {
            var result = Context.Invoke(iterator, "next").AsObject();
            if (result.Get(new PropertyKey("done")).AsBoolean())
                break;

            var match = result.Get(new PropertyKey("value")).AsObject();
            values.Add(JsOperations.ToString(match.Get(new PropertyKey(0u))) + "@" + JsOperations.ToString(match.Get(new PropertyKey("index"))));
        }

        Assert.Equal(["1@1", "22@3"], values);
        Assert.Equal("TypeError", ErrorName(() => context.StringMethod("a", "matchAll", context.NewRegExp("a"))));

        // A string is a global pattern; empty matches advance
        iterator = context.StringMethod("ab", "matchAll", "").AsObject();
        var count = 0;
        while (!Context.Invoke(iterator, "next").AsObject().Get(new PropertyKey("done")).AsBoolean())
        {
            count++;
        }

        Assert.Equal(3, count);
    }

    [Fact]
    public void ReplaceAll()
    {
        using var context = new Context();
        Assert.Equal("bbb", context.StringMethod("aaa", "replaceAll", "a", "b").AsString());
        Assert.Equal("-a-b-", context.StringMethod("ab", "replaceAll", "", "-").AsString());
        Assert.Equal("x$x", context.StringMethod("x.x", "replaceAll", ".", "$$").AsString());
        Assert.Equal("[a][a]", context.StringMethod("aa", "replaceAll", "a", "[$&]").AsString());
        Assert.Equal("b-b", context.StringMethod("a-a", "replaceAll", context.NewRegExp("a", "g"), "b").AsString());
        Assert.Equal("TypeError", ErrorName(() => context.StringMethod("aaa", "replaceAll", context.NewRegExp("a"), "b")));
        var replace = context.Realm.Intrinsics.StringPrototype.Get(new PropertyKey("replace"));
        Assert.Equal("TypeError", ErrorName(() => JsOperations.Call(replace, JsValue.Null, ["a", "b"])));
    }

    [Fact]
    public void LegacyStaticProperties()
    {
        using var context = new Context();
        var regexp = context.RegExp;
        Assert.Equal("", regexp.Get(new PropertyKey("$1")).AsString());
        Assert.Equal("", regexp.Get(new PropertyKey("lastMatch")).AsString());

        Context.Invoke(context.NewRegExp("(b)(c)"), "exec", "abcd");
        Assert.Equal("b", regexp.Get(new PropertyKey("$1")).AsString());
        Assert.Equal("c", regexp.Get(new PropertyKey("$2")).AsString());
        Assert.Equal("", regexp.Get(new PropertyKey("$3")).AsString());
        Assert.Equal("bc", regexp.Get(new PropertyKey("lastMatch")).AsString());
        Assert.Equal("bc", regexp.Get(new PropertyKey("$&")).AsString());
        Assert.Equal("c", regexp.Get(new PropertyKey("lastParen")).AsString());
        Assert.Equal("a", regexp.Get(new PropertyKey("leftContext")).AsString());
        Assert.Equal("d", regexp.Get(new PropertyKey("rightContext")).AsString());
        Assert.Equal("d", regexp.Get(new PropertyKey("$'")).AsString());
        Assert.Equal("abcd", regexp.Get(new PropertyKey("input")).AsString());

        regexp.Set(new PropertyKey("$_"), 42, throwOnFailure: true);
        Assert.Equal("42", regexp.Get(new PropertyKey("input")).AsString());

        // A match of an instance of a subclass makes them unavailable
        var subclassTarget = context.Global("Error").AsObject();
        var subclassInstance = regexp.Construct(["x"], subclassTarget).AsObject();
        JsOperations.Call(regexp.Get(new PropertyKey("prototype")).AsObject().Get(new PropertyKey("exec")), subclassInstance, ["x"]);
        Assert.Equal("TypeError", ErrorName(() => regexp.Get(new PropertyKey("$1"))));
        Assert.Equal("TypeError", ErrorName(() => regexp.Get(new PropertyKey("input"))));

        // The accessors only work on %RegExp% itself
        Assert.True(regexp.GetOwnProperty(new PropertyKey("$1"), out var descriptor));
        Assert.Equal("TypeError", ErrorName(() => JsOperations.Call(descriptor.Get, subclassTarget, [])));
    }

    [Fact]
    public void Compile()
    {
        using var context = new Context();
        var regexp = context.NewRegExp("a", "g");
        regexp.Set(new PropertyKey("lastIndex"), 3, throwOnFailure: true);
        Assert.Same(regexp, Context.Invoke(regexp, "compile", "b+", "i").AsObject());
        Assert.Equal("b+", regexp.Get(new PropertyKey("source")).AsString());
        Assert.Equal("i", regexp.Get(new PropertyKey("flags")).AsString());
        Assert.Equal(0, regexp.Get(new PropertyKey("lastIndex")).AsNumber());
        Context.Invoke(regexp, "compile", context.NewRegExp("c", "y"));
        Assert.Equal("y", regexp.Get(new PropertyKey("flags")).AsString());
        Assert.Equal("TypeError", ErrorName(() => Context.Invoke(regexp, "compile", context.NewRegExp("c"), "g")));
        Assert.Equal("SyntaxError", ErrorName(() => Context.Invoke(regexp, "compile", "(")));

        var subclassInstance = context.RegExp.Construct(["x"], context.Global("Error").AsObject()).AsObject();
        Assert.Equal("TypeError", ErrorName(() => JsOperations.Call(context.RegExp.Get(new PropertyKey("prototype")).AsObject().Get(new PropertyKey("compile")), subclassInstance, ["y"])));
    }

    [Fact]
    public void RegExpCreate_LiteralsCreateNewObjects()
    {
        using var context = new Context();
        var a = RegExpBuiltins.RegExpCreate(context.Realm, "a+", "g");
        var b = RegExpBuiltins.RegExpCreate(context.Realm, "a+", "g");
        Assert.NotSame(a, b);
        Assert.Same(a.Matcher, b.Matcher);
        Assert.Equal("/a+/g", JsOperations.ToString(Context.Invoke(a, "toString")));
    }

    [Fact]
    public void CustomExec()
    {
        using var context = new Context();
        var regexp = context.NewRegExp("a");
        var calls = 0;
        regexp.CreateDataPropertyOrThrow(new PropertyKey("exec"), context.Realm.CreateBuiltinFunction("exec", 1, (_, _, _, _) =>
        {
            calls++;
            return JsValue.Null;
        }));

        Assert.False(Context.Invoke(regexp, "test", "a").AsBoolean());
        Assert.Equal(1, calls);

        regexp.CreateDataPropertyOrThrow(new PropertyKey("exec"), context.Realm.CreateBuiltinFunction("exec", 1, (_, _, _, _) => 1));
        Assert.Equal("TypeError", ErrorName(() => Context.Invoke(regexp, "test", "a")));
    }
}
