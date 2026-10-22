using System.Text;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The String constructor, String.prototype and %StringIteratorPrototype% (https://tc39.es/ecma262/#sec-string-objects).</summary>
internal static class StringBuiltins
{
    private static readonly PropertyKey RawKey = new("raw");

    private static readonly BuiltinMethodTable ConstructorMethods =
    [
        new("fromCharCode", 1, FromCharCode),
        new("fromCodePoint", 1, FromCodePoint),
        new("raw", 1, Raw),
    ];

    private static readonly BuiltinMethodTable PrototypeMethods =
    [
        new("at", 1, At),
        new("charAt", 1, CharAt),
        new("charCodeAt", 1, CharCodeAt),
        new("codePointAt", 1, CodePointAt),
        new("concat", 1, Concat),
        new("endsWith", 1, EndsWith),
        new("includes", 1, Includes),
        new("indexOf", 1, IndexOf),
        new("isWellFormed", 0, IsWellFormed),
        new("lastIndexOf", 1, LastIndexOf),
        new("localeCompare", 1, LocaleCompare),
        new("normalize", 0, Normalize),
        new("padEnd", 1, (f, t, a, _) => Pad(t, a, atStart: false, "padEnd")),
        new("padStart", 1, (f, t, a, _) => Pad(t, a, atStart: true, "padStart")),
        new("repeat", 1, Repeat),
        new("slice", 2, Slice),
        new("startsWith", 1, StartsWith),
        new("substring", 2, Substring),
        new("toLocaleLowerCase", 0, (f, t, a, _) => StringOperations.ToLowerCase(ThisToString(t, "toLocaleLowerCase"))),
        new("toLocaleUpperCase", 0, (f, t, a, _) => StringOperations.ToUpperCase(ThisToString(t, "toLocaleUpperCase"))),
        new("toLowerCase", 0, (f, t, a, _) => StringOperations.ToLowerCase(ThisToString(t, "toLowerCase"))),
        new("toString", 0, (f, t, a, _) => JsValue.FromJsString(ThisStringValue(t, "toString"))),
        new("toUpperCase", 0, (f, t, a, _) => StringOperations.ToUpperCase(ThisToString(t, "toUpperCase"))),
        new("toWellFormed", 0, (f, t, a, _) => StringOperations.ToWellFormed(ThisToString(t, "toWellFormed"))),
        new("trim", 0, (f, t, a, _) => StringOperations.Trim(ThisToString(t, "trim"), start: true, end: true)),
    ];

    private static readonly BuiltinMethodTable PrototypeMethods2 =
    [
        new("valueOf", 0, (f, t, a, _) => JsValue.FromJsString(ThisStringValue(t, "valueOf"))),
        new(WellKnownSymbols.Iterator, 0, GetIterator),
    ];

    public static JsFunction Initialize(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;
        var prototype = intrinsics.StringPrototype;
        var constructor = realm.CreateBuiltinFunction("String", 1, Construct, isConstructor: true);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);
        realm.DefineMethods(constructor, ConstructorMethods);

        prototype.DefineBuiltIn("constructor", constructor);
        realm.DefineMethods(prototype, PrototypeMethods);
        var trimEnd = realm.DefineEagerMethod(prototype, "trimEnd", 0, (f, t, a, _) => StringOperations.Trim(ThisToString(t, "trimEnd"), start: false, end: true));
        var trimStart = realm.DefineEagerMethod(prototype, "trimStart", 0, (f, t, a, _) => StringOperations.Trim(ThisToString(t, "trimStart"), start: true, end: false));
        realm.DefineMethods(prototype, PrototypeMethods2);

        // Annex B (https://tc39.es/ecma262/#sec-additional-properties-of-the-string.prototype-object)
        realm.DefineMethod(prototype, "substr", 2, Substr);
        prototype.DefineBuiltIn("trimLeft", trimStart);
        prototype.DefineBuiltIn("trimRight", trimEnd);
        realm.DefineMethods(prototype, HtmlMethods);

        var iteratorPrototype = intrinsics.StringIteratorPrototype;
        realm.DefineMethod(iteratorPrototype, "next", 0, IteratorNext);
        iteratorPrototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "String Iterator", PropertyFlags.Configurable);

        intrinsics.AddGlobal("String", constructor);
        return constructor;
    }

    private static JsValue Arg(ReadOnlySpan<JsValue> arguments, int index) => arguments.Length > index ? arguments[index] : default;

    /// <summary>RequireObjectCoercible(this value) then ToString.</summary>
    private static string ThisToString(JsValue thisValue, string methodName)
    {
        if (thisValue.Cell is JsString str)
            return str.ToString();

        if (thisValue.IsNullOrUndefined)
        {
            ThrowHelper.ThrowTypeError("String.prototype." + methodName + " called on null or undefined");
        }

        return JsOperations.ToString(thisValue);
    }

    /// <summary>ThisStringValue(value)</summary>
    private static JsString ThisStringValue(JsValue value, string methodName)
    {
        if (value.Cell is JsString str)
            return str;

        if (value.Cell is JsStringObject obj)
            return obj.StringData;

        return ThrowHelper.ThrowTypeError<JsString>("String.prototype." + methodName + " requires that 'this' be a String");
    }

    /// <summary>String(value), new String(value)</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        JsString value;
        if (arguments.Length == 0)
        {
            value = JsString.Empty;
        }
        else
        {
            var argument = arguments[0];
            if (newTarget is null && argument.Cell is JsSymbol symbol)
                return symbol.ToString();

            value = JsOperations.ToJsString(argument);
        }

        if (newTarget is null)
            return JsValue.FromJsString(value);

        // StringCreate(s, prototype)
        var prototype = ObjectOperations.GetPrototypeFromConstructor(newTarget, i => i.StringPrototype);
        return new JsStringObject(prototype, value);
    }

    /// <summary>String.fromCharCode(...codeUnits)</summary>
    private static JsValue FromCharCode(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (arguments.Length == 1)
            return JsValue.FromJsString(JsString.Create((char)(ushort)JsOperations.ToInt32(arguments[0])));

        var chars = new char[arguments.Length];
        for (var i = 0; i < arguments.Length; i++)
        {
            chars[i] = (char)(ushort)JsOperations.ToInt32(arguments[i]);
        }

        return new string(chars);
    }

    /// <summary>String.fromCodePoint(...codePoints)</summary>
    private static JsValue FromCodePoint(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var builder = new StringBuilder(arguments.Length);
        foreach (var argument in arguments)
        {
            var next = JsOperations.ToNumber(argument);
            if (!double.IsFinite(next) || Math.Truncate(next) != next || next < 0 || next > 0x10FFFF)
            {
                ThrowHelper.ThrowRangeError("Invalid code point " + NumberFormatting.ToString(next));
            }

            StringOperations.AppendCodePoint(builder, (int)next);
        }

        StringOperations.CheckLength(builder.Length);
        return builder.ToString();
    }

    /// <summary>String.raw(template, ...substitutions)</summary>
    private static JsValue Raw(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var substitutionCount = Math.Max(arguments.Length - 1, 0);
        var cooked = JsOperations.ToObject(Arg(arguments, 0));
        var literals = JsOperations.ToObject(cooked.Get(RawKey));
        var literalCount = JsOperations.LengthOfArrayLike(literals);
        if (literalCount <= 0)
            return JsValue.FromJsString(JsString.Empty);

        var result = new StringBuilder();
        for (long nextIndex = 0; ; nextIndex++)
        {
            var nextLiteral = JsOperations.ToString(literals.Get(PropertyKey.FromInteger(nextIndex)));
            StringOperations.CheckLength((long)result.Length + nextLiteral.Length);
            result.Append(nextLiteral);
            if (nextIndex + 1 == literalCount)
                return result.ToString();

            if (nextIndex < substitutionCount)
            {
                var nextSubstitution = JsOperations.ToString(arguments[(int)nextIndex + 1]);
                StringOperations.CheckLength((long)result.Length + nextSubstitution.Length);
                result.Append(nextSubstitution);
            }
        }
    }

    /// <summary>String.prototype.at(index)</summary>
    private static JsValue At(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var s = ThisToString(thisValue, "at");
        var length = s.Length;
        var relativeIndex = JsOperations.ToIntegerOrInfinity(Arg(arguments, 0));
        var k = relativeIndex >= 0 ? relativeIndex : length + relativeIndex;
        if (k < 0 || k >= length)
            return JsValue.Undefined;

        return JsValue.FromJsString(JsString.Create(s[(int)k]));
    }

    /// <summary>String.prototype.charAt(pos)</summary>
    private static JsValue CharAt(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var s = ThisToString(thisValue, "charAt");
        var position = JsOperations.ToIntegerOrInfinity(Arg(arguments, 0));
        if (position < 0 || position >= s.Length)
            return JsValue.FromJsString(JsString.Empty);

        return JsValue.FromJsString(JsString.Create(s[(int)position]));
    }

    /// <summary>String.prototype.charCodeAt(pos)</summary>
    private static JsValue CharCodeAt(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var s = ThisToString(thisValue, "charCodeAt");
        var position = JsOperations.ToIntegerOrInfinity(Arg(arguments, 0));
        if (position < 0 || position >= s.Length)
            return double.NaN;

        return (int)s[(int)position];
    }

    /// <summary>String.prototype.codePointAt(pos)</summary>
    private static JsValue CodePointAt(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var s = ThisToString(thisValue, "codePointAt");
        var position = JsOperations.ToIntegerOrInfinity(Arg(arguments, 0));
        if (position < 0 || position >= s.Length)
            return JsValue.Undefined;

        return StringOperations.CodePointAt(s, (int)position, out _);
    }

    /// <summary>String.prototype.concat(...args)</summary>
    private static JsValue Concat(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.IsNullOrUndefined)
        {
            ThrowHelper.ThrowTypeError("String.prototype.concat called on null or undefined");
        }

        var result = JsOperations.ToJsString(thisValue);
        foreach (var argument in arguments)
        {
            result = StringOperations.Concat(result, JsOperations.ToJsString(argument));
        }

        return JsValue.FromJsString(result);
    }

    /// <summary>IsRegExp(argument)</summary>
    private static bool IsRegExp(JsValue argument)
    {
        if (argument.Cell is not JsObject obj)
            return false;

        var matcher = obj.Get(new PropertyKey(WellKnownSymbols.Match));
        if (!matcher.IsUndefined)
            return JsOperations.ToBoolean(matcher);

        return obj.Class == ObjectClass.RegExp;
    }

    private static string ToSearchString(JsValue searchString, string methodName)
    {
        if (IsRegExp(searchString))
        {
            ThrowHelper.ThrowTypeError("First argument to String.prototype." + methodName + " must not be a regular expression");
        }

        return JsOperations.ToString(searchString);
    }

    private static int Clamp(double value, int min, int max) => (int)Math.Clamp(value, min, max);

    /// <summary>String.prototype.endsWith(searchString [, endPosition])</summary>
    private static JsValue EndsWith(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var s = ThisToString(thisValue, "endsWith");
        var searchString = ToSearchString(Arg(arguments, 0), "endsWith");
        var length = s.Length;
        var endPosition = Arg(arguments, 1);
        var position = endPosition.IsUndefined ? length : JsOperations.ToIntegerOrInfinity(endPosition);
        var end = Clamp(position, 0, length);
        if (searchString.Length == 0)
            return true;

        var start = end - searchString.Length;
        if (start < 0)
            return false;

        return s.AsSpan(start, searchString.Length).SequenceEqual(searchString);
    }

    /// <summary>String.prototype.includes(searchString [, position])</summary>
    private static JsValue Includes(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var s = ThisToString(thisValue, "includes");
        var searchString = ToSearchString(Arg(arguments, 0), "includes");
        var position = JsOperations.ToIntegerOrInfinity(Arg(arguments, 1));
        var start = Clamp(position, 0, s.Length);
        return s.AsSpan(start).IndexOf(searchString, StringComparison.Ordinal) >= 0;
    }

    /// <summary>String.prototype.indexOf(searchString [, position])</summary>
    private static JsValue IndexOf(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var s = ThisToString(thisValue, "indexOf");
        var searchString = JsOperations.ToString(Arg(arguments, 0));
        var position = JsOperations.ToIntegerOrInfinity(Arg(arguments, 1));
        var start = Clamp(position, 0, s.Length);
        var index = s.AsSpan(start).IndexOf(searchString, StringComparison.Ordinal);
        return index < 0 ? -1 : index + start;
    }

    /// <summary>String.prototype.isWellFormed()</summary>
    private static JsValue IsWellFormed(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return StringOperations.IsWellFormed(ThisToString(thisValue, "isWellFormed"));
    }

    /// <summary>String.prototype.lastIndexOf(searchString [, position])</summary>
    private static JsValue LastIndexOf(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var s = ThisToString(thisValue, "lastIndexOf");
        var searchString = JsOperations.ToString(Arg(arguments, 0));
        var numberPosition = JsOperations.ToNumber(Arg(arguments, 1));
        var position = double.IsNaN(numberPosition) ? double.PositiveInfinity : JsOperations.ToIntegerOrInfinity(numberPosition);
        var length = s.Length;
        var searchLength = searchString.Length;
        if (searchLength > length)
            return -1;

        var start = Clamp(position, 0, length - searchLength);
        if (searchLength == 0)
            return start;

        return s.AsSpan(0, start + searchLength).LastIndexOf(searchString, StringComparison.Ordinal);
    }

    /// <summary>String.prototype.localeCompare(that): without ECMA-402, the NFC normalizations are compared by code units.</summary>
    private static JsValue LocaleCompare(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var s = ThisToString(thisValue, "localeCompare");
        var that = JsOperations.ToString(Arg(arguments, 0));
        var result = string.CompareOrdinal(StringOperations.Normalize(s, NormalizationForm.FormC), StringOperations.Normalize(that, NormalizationForm.FormC));
        return Math.Sign(result);
    }

    /// <summary>String.prototype.normalize([form])</summary>
    private static JsValue Normalize(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var s = ThisToString(thisValue, "normalize");
        var formArgument = Arg(arguments, 0);
        var form = NormalizationForm.FormC;
        if (!formArgument.IsUndefined)
        {
            form = JsOperations.ToString(formArgument) switch
            {
                "NFC" => NormalizationForm.FormC,
                "NFD" => NormalizationForm.FormD,
                "NFKC" => NormalizationForm.FormKC,
                "NFKD" => NormalizationForm.FormKD,
                _ => ThrowHelper.ThrowRangeError<NormalizationForm>("The normalization form should be one of NFC, NFD, NFKC, NFKD."),
            };
        }

        return StringOperations.Normalize(s, form);
    }

    /// <summary>StringPaddingBuiltinsImpl(O, maxLength, fillString, placement)</summary>
    private static JsValue Pad(JsValue thisValue, ReadOnlySpan<JsValue> arguments, bool atStart, string methodName)
    {
        var s = ThisToString(thisValue, methodName);
        var maxLength = JsOperations.ToLength(Arg(arguments, 0));
        var stringLength = s.Length;
        if (maxLength <= stringLength)
            return s;

        var fillArgument = Arg(arguments, 1);
        var filler = fillArgument.IsUndefined ? " " : JsOperations.ToString(fillArgument);
        if (filler.Length == 0)
            return s;

        StringOperations.CheckLength((long)maxLength);
        var fillLength = (int)maxLength - stringLength;
        var result = new StringBuilder((int)maxLength);
        if (!atStart)
        {
            result.Append(s);
        }

        while (fillLength > 0)
        {
            var count = Math.Min(fillLength, filler.Length);
            result.Append(filler, 0, count);
            fillLength -= count;
        }

        if (atStart)
        {
            result.Append(s);
        }

        return result.ToString();
    }

    /// <summary>String.prototype.repeat(count)</summary>
    private static JsValue Repeat(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var s = ThisToString(thisValue, "repeat");
        var n = JsOperations.ToIntegerOrInfinity(Arg(arguments, 0));
        if (n < 0 || double.IsPositiveInfinity(n))
        {
            ThrowHelper.ThrowRangeError("Invalid count value: " + NumberFormatting.ToString(n));
        }

        if (n == 0 || s.Length == 0)
            return JsValue.FromJsString(JsString.Empty);

        if (n * s.Length > JsString.MaxLength)
        {
            StringOperations.ThrowInvalidLength();
        }

        var count = (int)n;
        if (s.Length == 1)
            return new string(s[0], count);

        return string.Create(s.Length * count, s, (span, value) =>
        {
            for (var i = 0; i < span.Length; i += value.Length)
            {
                value.CopyTo(span[i..]);
            }
        });
    }

    /// <summary>String.prototype.slice(start, end)</summary>
    private static JsValue Slice(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var s = ThisToString(thisValue, "slice");
        var length = s.Length;
        var from = RelativeIndex(JsOperations.ToIntegerOrInfinity(Arg(arguments, 0)), length);
        var endArgument = Arg(arguments, 1);
        var to = endArgument.IsUndefined ? length : RelativeIndex(JsOperations.ToIntegerOrInfinity(endArgument), length);
        if (from >= to)
            return JsValue.FromJsString(JsString.Empty);

        return s[from..to];
    }

    /// <summary>The resolution of a relative index: negative values count from the end, the result is clamped to [0, length].</summary>
    private static int RelativeIndex(double relative, int length)
    {
        if (relative < 0)
            return (int)Math.Max(length + relative, 0);

        return (int)Math.Min(relative, length);
    }

    /// <summary>String.prototype.startsWith(searchString [, position])</summary>
    private static JsValue StartsWith(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var s = ThisToString(thisValue, "startsWith");
        var searchString = ToSearchString(Arg(arguments, 0), "startsWith");
        var position = JsOperations.ToIntegerOrInfinity(Arg(arguments, 1));
        var start = Clamp(position, 0, s.Length);
        if (searchString.Length == 0)
            return true;

        if ((long)start + searchString.Length > s.Length)
            return false;

        return s.AsSpan(start, searchString.Length).SequenceEqual(searchString);
    }

    /// <summary>String.prototype.substring(start, end)</summary>
    private static JsValue Substring(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var s = ThisToString(thisValue, "substring");
        var length = s.Length;
        var intStart = JsOperations.ToIntegerOrInfinity(Arg(arguments, 0));
        var endArgument = Arg(arguments, 1);
        var intEnd = endArgument.IsUndefined ? length : JsOperations.ToIntegerOrInfinity(endArgument);
        var finalStart = Clamp(intStart, 0, length);
        var finalEnd = Clamp(intEnd, 0, length);
        var from = Math.Min(finalStart, finalEnd);
        var to = Math.Max(finalStart, finalEnd);
        return s[from..to];
    }

    /// <summary>String.prototype.substr(start, length) (Annex B)</summary>
    private static JsValue Substr(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var s = ThisToString(thisValue, "substr");
        var size = s.Length;
        var intStart = RelativeIndex(JsOperations.ToIntegerOrInfinity(Arg(arguments, 0)), size);
        var lengthArgument = Arg(arguments, 1);
        var intLength = lengthArgument.IsUndefined ? size : JsOperations.ToIntegerOrInfinity(lengthArgument);
        var clampedLength = Clamp(intLength, 0, size);
        var intEnd = Math.Min(intStart + clampedLength, size);
        if (intStart >= intEnd)
            return JsValue.FromJsString(JsString.Empty);

        return s[intStart..intEnd];
    }

    // The HTML methods of Annex B
    private static readonly BuiltinMethodTable HtmlMethods =
    [
        HtmlMethod("anchor", "a", "name"),
        HtmlMethod("big", "big", attribute: null),
        HtmlMethod("blink", "blink", attribute: null),
        HtmlMethod("bold", "b", attribute: null),
        HtmlMethod("fixed", "tt", attribute: null),
        HtmlMethod("fontcolor", "font", "color"),
        HtmlMethod("fontsize", "font", "size"),
        HtmlMethod("italics", "i", attribute: null),
        HtmlMethod("link", "a", "href"),
        HtmlMethod("small", "small", attribute: null),
        HtmlMethod("strike", "strike", attribute: null),
        HtmlMethod("sub", "sub", attribute: null),
        HtmlMethod("sup", "sup", attribute: null),
    ];

    private static BuiltinMethod HtmlMethod(string name, string tag, string? attribute)
    {
        return new(name, attribute is null ? 0 : 1, (f, t, a, _) => CreateHtml(t, name, tag, attribute, Arg(a, 0)));
    }

    /// <summary>CreateHTML(string, tag, attribute, value) (Annex B)</summary>
    private static JsValue CreateHtml(JsValue thisValue, string methodName, string tag, string? attribute, JsValue value)
    {
        var s = ThisToString(thisValue, methodName);
        var result = new StringBuilder();
        result.Append('<').Append(tag);
        if (attribute is not null)
        {
            var v = JsOperations.ToString(value);
            result.Append(' ').Append(attribute).Append("=\"");
            foreach (var c in v)
            {
                if (c == '"')
                {
                    result.Append("&quot;");
                }
                else
                {
                    result.Append(c);
                }
            }

            result.Append('"');
        }

        result.Append('>');
        StringOperations.CheckLength((long)result.Length + s.Length + tag.Length + 3);
        result.Append(s).Append("</").Append(tag).Append('>');
        return result.ToString();
    }

    /// <summary>String.prototype[%Symbol.iterator%]()</summary>
    private static JsValue GetIterator(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var s = ThisToString(thisValue, "[Symbol.iterator]");
        return new JsStringIterator(function.Realm.Intrinsics.StringIteratorPrototype, s);
    }

    /// <summary>%StringIteratorPrototype%.next()</summary>
    private static JsValue IteratorNext(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is not JsStringIterator iterator)
            return ThrowHelper.ThrowTypeError<JsValue>("%StringIteratorPrototype%.next requires that 'this' be a String Iterator");

        var realm = function.Realm;
        var s = iterator.IteratedString;
        if (s is null || iterator.Position >= s.Length)
        {
            iterator.IteratedString = null;
            return IteratorOperations.CreateIteratorResultObject(realm, JsValue.Undefined, done: true);
        }

        var position = iterator.Position;
        StringOperations.CodePointAt(s, position, out var length);
        iterator.Position = position + length;
        var result = length == 1 ? JsString.Create(s[position]) : JsString.Create(s.Substring(position, length));
        return IteratorOperations.CreateIteratorResultObject(realm, JsValue.FromJsString(result), done: false);
    }
}

/// <summary>A String Iterator object (CreateStringIterator): the iterated string and the position of the next code point.</summary>
internal sealed class JsStringIterator(JsObject prototype, string value) : JsObject(prototype, ObjectClass.Iterator)
{
    /// <summary>Gets or sets the iterated string, or null once the iterator is done.</summary>
    public string? IteratedString { get; set; } = value;

    public int Position { get; set; }
}
