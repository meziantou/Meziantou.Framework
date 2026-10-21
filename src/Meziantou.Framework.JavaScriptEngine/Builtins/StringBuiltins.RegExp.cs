using System.Text;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The methods of String.prototype that delegate to the regular expressions: match, matchAll, replace, replaceAll,
/// search and split (https://tc39.es/ecma262/#sec-properties-of-the-string-prototype-object).
/// </summary>
internal static class StringRegExpBuiltins
{
    private static readonly PropertyKey FlagsKey = new("flags");

    public static void Initialize(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;
        var prototype = intrinsics.StringPrototype;
        realm.DefineMethod(prototype, "match", 1, Match);
        realm.DefineMethod(prototype, "matchAll", 1, MatchAll);
        realm.DefineMethod(prototype, "replace", 2, Replace);
        realm.DefineMethod(prototype, "replaceAll", 2, ReplaceAll);
        realm.DefineMethod(prototype, "search", 1, Search);
        realm.DefineMethod(prototype, "split", 2, Split);
    }

    private static JsValue RequireObjectCoercible(JsValue thisValue, string methodName)
    {
        JsOperations.RequireObjectCoercible(thisValue, "String.prototype." + methodName);
        return thisValue;
    }

    /// <summary>String.prototype.match(regexp)</summary>
    private static JsValue Match(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        RequireObjectCoercible(thisValue, "match");
        var regexp = arguments.Length > 0 ? arguments[0] : default;
        if (regexp.IsObject)
        {
            var matcher = JsOperations.GetMethod(regexp, new PropertyKey(WellKnownSymbols.Match));
            if (matcher is not null)
                return matcher.Call(regexp, [thisValue]);
        }

        var input = JsOperations.ToString(thisValue);
        var rx = RegExpBuiltins.RegExpCreate(regexp, JsValue.Undefined);
        return JsOperations.Invoke(rx, new PropertyKey(WellKnownSymbols.Match), [input]);
    }

    /// <summary>String.prototype.matchAll(regexp)</summary>
    private static JsValue MatchAll(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        RequireObjectCoercible(thisValue, "matchAll");
        var regexp = arguments.Length > 0 ? arguments[0] : default;
        if (regexp.Cell is JsObject regexpObject)
        {
            if (RegExpBuiltins.IsRegExp(regexp))
            {
                var flags = regexpObject.Get(FlagsKey);
                JsOperations.RequireObjectCoercible(flags);
                if (!JsOperations.ToString(flags).Contains('g', StringComparison.Ordinal))
                    return ThrowHelper.ThrowTypeError<JsValue>("String.prototype.matchAll called with a non-global RegExp argument");
            }

            var matcher = JsOperations.GetMethod(regexp, new PropertyKey(WellKnownSymbols.MatchAll));
            if (matcher is not null)
                return matcher.Call(regexp, [thisValue]);
        }

        var input = JsOperations.ToString(thisValue);
        var rx = RegExpBuiltins.RegExpCreate(regexp, "g");
        return JsOperations.Invoke(rx, new PropertyKey(WellKnownSymbols.MatchAll), [input]);
    }

    /// <summary>String.prototype.replace(searchValue, replaceValue)</summary>
    private static JsValue Replace(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        RequireObjectCoercible(thisValue, "replace");
        var searchValue = arguments.Length > 0 ? arguments[0] : default;
        var replaceValue = arguments.Length > 1 ? arguments[1] : default;
        if (searchValue.IsObject)
        {
            var replacer = JsOperations.GetMethod(searchValue, new PropertyKey(WellKnownSymbols.Replace));
            if (replacer is not null)
                return replacer.Call(searchValue, [thisValue, replaceValue]);
        }

        var input = JsOperations.ToString(thisValue);
        var searchString = JsOperations.ToString(searchValue);
        var functionalReplace = replaceValue.IsCallable;
        string? template = null;
        if (!functionalReplace)
        {
            template = JsOperations.ToString(replaceValue);
        }

        var position = input.IndexOf(searchString, StringComparison.Ordinal);
        if (position < 0)
            return input;

        string replacement;
        if (functionalReplace)
        {
            replacement = JsOperations.ToString(JsOperations.Call(replaceValue, JsValue.Undefined, [searchString, position, input]));
        }
        else
        {
            replacement = RegExpBuiltins.GetSubstitution(searchString, input, position, [], namedCaptures: null, template!);
        }

        return string.Concat(input.AsSpan(0, position), replacement, input.AsSpan(position + searchString.Length));
    }

    /// <summary>String.prototype.replaceAll(searchValue, replaceValue)</summary>
    private static JsValue ReplaceAll(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        RequireObjectCoercible(thisValue, "replaceAll");
        var searchValue = arguments.Length > 0 ? arguments[0] : default;
        var replaceValue = arguments.Length > 1 ? arguments[1] : default;
        if (searchValue.Cell is JsObject searchObject)
        {
            if (RegExpBuiltins.IsRegExp(searchValue))
            {
                var flags = searchObject.Get(FlagsKey);
                JsOperations.RequireObjectCoercible(flags);
                if (!JsOperations.ToString(flags).Contains('g', StringComparison.Ordinal))
                    return ThrowHelper.ThrowTypeError<JsValue>("String.prototype.replaceAll called with a non-global RegExp argument");
            }

            var replacer = JsOperations.GetMethod(searchValue, new PropertyKey(WellKnownSymbols.Replace));
            if (replacer is not null)
                return replacer.Call(searchValue, [thisValue, replaceValue]);
        }

        var input = JsOperations.ToString(thisValue);
        var searchString = JsOperations.ToString(searchValue);
        var functionalReplace = replaceValue.IsCallable;
        string? template = null;
        if (!functionalReplace)
        {
            template = JsOperations.ToString(replaceValue);
        }

        var searchLength = searchString.Length;
        var advanceBy = Math.Max(1, searchLength);
        var positions = new List<int>();
        var position = StringIndexOf(input, searchString, 0);
        while (position >= 0)
        {
            positions.Add(position);
            position = StringIndexOf(input, searchString, position + advanceBy);
        }

        var endOfLastMatch = 0;
        var result = new StringBuilder();
        foreach (var matchPosition in positions)
        {
            result.Append(input, endOfLastMatch, matchPosition - endOfLastMatch);
            string replacement;
            if (functionalReplace)
            {
                replacement = JsOperations.ToString(JsOperations.Call(replaceValue, JsValue.Undefined, [searchString, matchPosition, input]));
            }
            else
            {
                replacement = RegExpBuiltins.GetSubstitution(searchString, input, matchPosition, [], namedCaptures: null, template!);
            }

            result.Append(replacement);
            endOfLastMatch = matchPosition + searchLength;
        }

        if (endOfLastMatch < input.Length)
        {
            result.Append(input, endOfLastMatch, input.Length - endOfLastMatch);
        }

        return result.ToString();
    }

    /// <summary>String.prototype.search(regexp)</summary>
    private static JsValue Search(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        RequireObjectCoercible(thisValue, "search");
        var regexp = arguments.Length > 0 ? arguments[0] : default;
        if (regexp.IsObject)
        {
            var searcher = JsOperations.GetMethod(regexp, new PropertyKey(WellKnownSymbols.Search));
            if (searcher is not null)
                return searcher.Call(regexp, [thisValue]);
        }

        var input = JsOperations.ToString(thisValue);
        var rx = RegExpBuiltins.RegExpCreate(regexp, JsValue.Undefined);
        return JsOperations.Invoke(rx, new PropertyKey(WellKnownSymbols.Search), [input]);
    }

    /// <summary>String.prototype.split(separator, limit)</summary>
    private static JsValue Split(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        RequireObjectCoercible(thisValue, "split");
        var separator = arguments.Length > 0 ? arguments[0] : default;
        var limit = arguments.Length > 1 ? arguments[1] : default;
        if (separator.IsObject)
        {
            var splitter = JsOperations.GetMethod(separator, new PropertyKey(WellKnownSymbols.Split));
            if (splitter is not null)
                return splitter.Call(separator, [thisValue, limit]);
        }

        var input = JsOperations.ToString(thisValue);
        var lim = limit.IsUndefined ? uint.MaxValue : JsOperations.ToUint32(limit);
        var separatorString = JsOperations.ToString(separator);
        var realm = function.Realm;
        var array = new JsArray(realm.Intrinsics.ArrayPrototype);
        if (lim == 0)
            return array;

        if (separator.IsUndefined)
        {
            array.Push(input);
            return array;
        }

        var separatorLength = separatorString.Length;
        if (separatorLength == 0)
        {
            var outLength = (int)Math.Min(lim, (uint)input.Length);
            for (var i = 0; i < outLength; i++)
            {
                array.Push(JsValue.FromJsString(JsString.Create(input[i])));
            }

            return array;
        }

        if (input.Length == 0)
        {
            array.Push(input);
            return array;
        }

        var searchStart = 0;
        var matchIndex = StringIndexOf(input, separatorString, 0);
        while (matchIndex >= 0)
        {
            array.Push(input[searchStart..matchIndex]);
            if (array.Length == lim)
                return array;

            searchStart = matchIndex + separatorLength;
            matchIndex = StringIndexOf(input, separatorString, searchStart);
        }

        array.Push(input[searchStart..]);
        return array;
    }

    /// <summary>StringIndexOf(string, searchValue, fromIndex), or -1 for not-found.</summary>
    private static int StringIndexOf(string input, string searchValue, int fromIndex)
    {
        if (fromIndex > input.Length)
            return -1;

        return input.AsSpan(fromIndex).IndexOf(searchValue, StringComparison.Ordinal) is var index && index >= 0 ? index + fromIndex : -1;
    }
}
