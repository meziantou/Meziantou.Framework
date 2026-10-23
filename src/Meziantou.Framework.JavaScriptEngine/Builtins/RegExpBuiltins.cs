using System.Globalization;
using System.Text;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>RegExp (https://tc39.es/ecma262/#sec-regexp-regular-expression-objects), with the legacy RegExp features of Annex B.</summary>
internal static class RegExpBuiltins
{
    private static readonly PropertyKey LastIndexKey = JsRegExpObject.LastIndexKey;
    private static readonly PropertyKey ExecKey = new("exec");
    private static readonly PropertyKey FlagsKey = new("flags");
    private static readonly PropertyKey SourceKey = new("source");
    private static readonly PropertyKey IndexKey = new("index");
    private static readonly PropertyKey InputKey = new("input");
    private static readonly PropertyKey GroupsKey = new("groups");
    private static readonly PropertyKey IndicesKey = new("indices");
    private static readonly PropertyKey ConstructorKey = new("constructor");
    private static readonly PropertyKey ZeroKey = new(0u);

    private static readonly BuiltinMethodTable PrototypeMethods =
    [
        new(WellKnownSymbols.Match, 1, SymbolMatch),
        new(WellKnownSymbols.MatchAll, 1, SymbolMatchAll),
    ];

    private static readonly BuiltinMethodTable PrototypeMethods2 =
    [
        new(WellKnownSymbols.Replace, 2, SymbolReplace),
        new(WellKnownSymbols.Search, 1, SymbolSearch),
    ];

    private static readonly BuiltinMethodTable PrototypeMethods3 =
    [
        new("test", 1, Test),
        new("toString", 0, ToString),
    ];

    public static (JsFunction Constructor, JsObject Prototype, JsFunction Exec, JsObject IteratorPrototype) Initialize(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;
        var prototype = new JsObject(intrinsics.ObjectPrototype);
        var constructor = realm.CreateBuiltinFunction("RegExp", 2, Construct, isConstructor: true);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);
        prototype.DefineBuiltIn(ConstructorKey, constructor);

        realm.DefineMethod(constructor, "escape", 1, Escape);
        realm.DefineAccessor(constructor, WellKnownSymbols.Species, GetSpecies, setter: null);
        DefineLegacyAccessors(realm, constructor);

        var exec = realm.DefineEagerMethod(prototype, "exec", 1, Exec);
        realm.DefineAccessor(prototype, "dotAll", (f, t, _, _) => HasFlag(f, t, 's'), setter: null);
        realm.DefineAccessor(prototype, "flags", GetFlags, setter: null);
        realm.DefineAccessor(prototype, "global", (f, t, _, _) => HasFlag(f, t, 'g'), setter: null);
        realm.DefineAccessor(prototype, "hasIndices", (f, t, _, _) => HasFlag(f, t, 'd'), setter: null);
        realm.DefineAccessor(prototype, "ignoreCase", (f, t, _, _) => HasFlag(f, t, 'i'), setter: null);
        realm.DefineMethods(prototype, PrototypeMethods);
        realm.DefineAccessor(prototype, "multiline", (f, t, _, _) => HasFlag(f, t, 'm'), setter: null);
        realm.DefineMethods(prototype, PrototypeMethods2);
        realm.DefineAccessor(prototype, "source", GetSource, setter: null);
        realm.DefineMethod(prototype, WellKnownSymbols.Split, 2, SymbolSplit);
        realm.DefineAccessor(prototype, "sticky", (f, t, _, _) => HasFlag(f, t, 'y'), setter: null);
        realm.DefineMethods(prototype, PrototypeMethods3);
        realm.DefineAccessor(prototype, "unicode", (f, t, _, _) => HasFlag(f, t, 'u'), setter: null);
        realm.DefineAccessor(prototype, "unicodeSets", (f, t, _, _) => HasFlag(f, t, 'v'), setter: null);
        realm.DefineMethod(prototype, "compile", 2, Compile);

        var iteratorPrototype = new JsObject(intrinsics.IteratorPrototype);
        realm.DefineMethod(iteratorPrototype, "next", 0, IteratorNext);
        iteratorPrototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "RegExp String Iterator", PropertyFlags.Configurable);

        intrinsics.AddGlobal("RegExp", constructor);
        return (constructor, prototype, exec, iteratorPrototype);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Creation (https://tc39.es/ecma262/#sec-abstract-operations-for-regexp-creation)
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>RegExpCreate(P, F) in a realm: used for the regular expression literals (each evaluation creates an object).</summary>
    public static JsRegExpObject RegExpCreate(JsRealm realm, string pattern, string flags)
    {
        var obj = new JsRegExpObject(realm.Intrinsics.RegExpPrototype, realm, legacyFeaturesEnabled: true);
        return Initialize(obj, pattern, flags);
    }

    /// <summary>RegExpCreate(P, F) in the current realm.</summary>
    public static JsRegExpObject RegExpCreate(JsValue pattern, JsValue flags)
    {
        var realm = CurrentRealm;
        var obj = new JsRegExpObject(realm.Intrinsics.RegExpPrototype, realm, legacyFeaturesEnabled: true);
        return Initialize(obj, pattern, flags);
    }

    private static JsRealm CurrentRealm => JsEngine.Current!.CurrentRealm!;

    /// <summary>RegExpAlloc(newTarget)</summary>
    private static JsRegExpObject Alloc(JsObject newTarget)
    {
        var prototype = ObjectOperations.GetPrototypeFromConstructor(newTarget, i => i.RegExpPrototype);
        var realm = CurrentRealm;
        return new JsRegExpObject(prototype, realm, ReferenceEquals(newTarget, realm.Intrinsics.RegExp));
    }

    /// <summary>RegExpInitialize(obj, pattern, flags)</summary>
    private static JsRegExpObject Initialize(JsRegExpObject obj, JsValue pattern, JsValue flags)
    {
        var patternText = pattern.IsUndefined ? "" : JsOperations.ToString(pattern);
        var flagsText = flags.IsUndefined ? "" : JsOperations.ToString(flags);
        return Initialize(obj, patternText, flagsText);
    }

    private static JsRegExpObject Initialize(JsRegExpObject obj, string pattern, string flags)
    {
        if (!RegExpFlagsExtensions.TryParse(flags, out var parsedFlags))
        {
            ThrowHelper.ThrowSyntaxError($"Invalid regular expression flags '{flags}'");
        }

        var program = RegExpProgram.GetOrCompile(pattern, parsedFlags, out var error);
        if (program is null)
        {
            ThrowHelper.ThrowSyntaxError($"Invalid regular expression: /{pattern}/{flags}: {error}");
        }

        obj.Initialize(pattern, flags, parsedFlags, program);
        obj.Set(LastIndexKey, 0, throwOnFailure: true);
        return obj;
    }

    /// <summary>IsRegExp(argument)</summary>
    public static bool IsRegExp(JsValue argument)
    {
        if (argument.Cell is not JsObject obj)
            return false;

        var matcher = obj.Get(new PropertyKey(WellKnownSymbols.Match));
        if (!matcher.IsUndefined)
            return JsOperations.ToBoolean(matcher);

        return obj is JsRegExpObject;
    }

    /// <summary>RegExp(pattern, flags)</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var pattern = arguments.Length > 0 ? arguments[0] : default;
        var flags = arguments.Length > 1 ? arguments[1] : default;
        var patternIsRegExp = IsRegExp(pattern);
        if (newTarget is null)
        {
            newTarget = function;
            if (patternIsRegExp && flags.IsUndefined)
            {
                var patternConstructor = pattern.AsObject().Get(ConstructorKey);
                if (patternConstructor.Cell == newTarget)
                    return pattern;
            }
        }

        JsValue source;
        if (pattern.Cell is JsRegExpObject regexp)
        {
            source = regexp.OriginalSource;
            if (flags.IsUndefined)
            {
                flags = regexp.OriginalFlags;
            }
        }
        else if (patternIsRegExp)
        {
            var obj = pattern.AsObject();
            source = obj.Get(SourceKey);
            if (flags.IsUndefined)
            {
                flags = obj.Get(FlagsKey);
            }
        }
        else
        {
            source = pattern;
        }

        var result = Alloc(newTarget);
        return Initialize(result, source, flags);
    }

    /// <summary>get RegExp[%Symbol.species%]</summary>
    private static JsValue GetSpecies(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget) => thisValue;

    /// <summary>RegExp.escape(string)</summary>
    private static JsValue Escape(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var value = arguments.Length > 0 ? arguments[0] : default;
        if (!value.IsString)
            return ThrowHelper.ThrowTypeError<JsValue>("RegExp.escape requires a string");

        var text = value.AsString();
        var builder = new StringBuilder(text.Length * 2);
        for (var i = 0; i < text.Length; i++)
        {
            int codePoint = text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                codePoint = char.ConvertToUtf32(text[i], text[i + 1]);
            }

            if (builder.Length == 0 && (char.IsAsciiDigit(text[i]) || char.IsAsciiLetter(text[i])))
            {
                // A leading digit or letter is escaped, so that the result can follow \0, \1 or \c
                builder.Append("\\x").Append(codePoint.ToString("x2", CultureInfo.InvariantCulture));
            }
            else
            {
                EncodeForRegExpEscape(builder, codePoint);
            }

            if (codePoint > 0xFFFF)
            {
                i++;
            }
        }

        return builder.ToString();
    }

    /// <summary>EncodeForRegExpEscape(codePoint)</summary>
    private static void EncodeForRegExpEscape(StringBuilder builder, int codePoint)
    {
        if (codePoint is '^' or '$' or '\\' or '.' or '*' or '+' or '?' or '(' or ')' or '[' or ']' or '{' or '}' or '|' or '/')
        {
            builder.Append('\\').Append((char)codePoint);
            return;
        }

        var controlEscape = codePoint switch
        {
            '\t' => 't',
            '\n' => 'n',
            '\v' => 'v',
            '\f' => 'f',
            '\r' => 'r',
            _ => '\0',
        };

        if (controlEscape != '\0')
        {
            builder.Append('\\').Append(controlEscape);
            return;
        }

        if (",-=<>#&!%:;@~'`\"".Contains((char)codePoint, StringComparison.Ordinal) && codePoint < 0x80
            || IsWhiteSpaceOrLineTerminator(codePoint)
            || codePoint is >= 0xD800 and <= 0xDFFF)
        {
            if (codePoint <= 0xFF)
            {
                builder.Append("\\x").Append(codePoint.ToString("x2", CultureInfo.InvariantCulture));
                return;
            }

            if (codePoint > 0xFFFF)
            {
                var value = char.ConvertFromUtf32(codePoint);
                builder.Append("\\u").Append(((int)value[0]).ToString("x4", CultureInfo.InvariantCulture));
                builder.Append("\\u").Append(((int)value[1]).ToString("x4", CultureInfo.InvariantCulture));
            }
            else
            {
                builder.Append("\\u").Append(codePoint.ToString("x4", CultureInfo.InvariantCulture));
            }

            return;
        }

        if (codePoint > 0xFFFF)
        {
            builder.Append(char.ConvertFromUtf32(codePoint));
        }
        else
        {
            builder.Append((char)codePoint);
        }
    }

    private static bool IsWhiteSpaceOrLineTerminator(int codePoint)
    {
        return codePoint is '\t' or '\v' or '\f' or '\uFEFF' or '\n' or '\r' or '\u2028' or '\u2029'
            || (codePoint <= 0xFFFF && UnicodeProperties.GetGeneralCategory(codePoint) == UnicodeCategory.SpaceSeparator);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Matching (https://tc39.es/ecma262/#sec-regexp-abstract-operations)
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>RegExp.prototype.exec(string)</summary>
    private static JsValue Exec(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is not JsRegExpObject { Matcher: not null } regexp)
            return ThrowHelper.ThrowTypeError<JsValue>("RegExp.prototype.exec requires that 'this' be a RegExp object");

        var input = JsOperations.ToString(arguments.Length > 0 ? arguments[0] : default);
        return BuiltinExec(function.Realm, regexp, input);
    }

    /// <summary>RegExpBuiltinExec(R, S)</summary>
    private static JsValue BuiltinExec(JsRealm realm, JsRegExpObject regexp, string input)
    {
        var captures = BuiltinExecCore(realm, regexp, input);
        if (captures is null)
            return JsValue.Null;

        return CreateMatchResult(realm, regexp, input, captures);
    }

    /// <summary>RegExpBuiltinExec(R, S) without creating the result array: the capture slots, or null.</summary>
    /// <param name="currentRealm">The current realm: the realm of the exec function.</param>
    /// <param name="regexp">R</param>
    /// <param name="input">S</param>
    private static int[]? BuiltinExecCore(JsRealm currentRealm, JsRegExpObject regexp, string input)
    {
        var length = input.Length;
        var lastIndex = JsOperations.ToLength(regexp.Get(LastIndexKey));
        var flags = regexp.RecordFlags;
        var global = (flags & RegExpFlags.Global) != 0;
        var sticky = (flags & RegExpFlags.Sticky) != 0;
        if (!global && !sticky)
        {
            lastIndex = 0;
        }

        int[]? captures = null;
        if (lastIndex <= length)
        {
            captures = regexp.Matcher!.Exec(input, (int)lastIndex, sticky, JsEngine.Current);
        }

        if (captures is null)
        {
            if (global || sticky)
            {
                regexp.Set(LastIndexKey, 0, throwOnFailure: true);
            }

            return null;
        }

        if (global || sticky)
        {
            regexp.Set(LastIndexKey, captures[1], throwOnFailure: true);
        }

        UpdateLegacyStaticProperties(currentRealm, regexp, input, captures);
        return captures;
    }

    /// <summary>The legacy static properties of %RegExp% follow the successful matches of the regular expressions of its realm.</summary>
    private static void UpdateLegacyStaticProperties(JsRealm currentRealm, JsRegExpObject regexp, string input, int[] captures)
    {
        if (ReferenceEquals(currentRealm, regexp.Realm))
        {
            if (regexp.LegacyFeaturesEnabled)
            {
                currentRealm.Intrinsics.RegExpLegacyStatics.Update(input, captures);
            }
            else
            {
                currentRealm.Intrinsics.RegExpLegacyStatics.Invalidate();
            }
        }
    }

    /// <summary>The Array RegExpBuiltinExec returns: the captures, index, input, groups and indices.</summary>
    private static JsArray CreateMatchResult(JsRealm realm, JsRegExpObject regexp, string input, int[] captures)
    {
        var program = regexp.Matcher!;
        var groupCount = captures.Length / 2;
        var array = new JsArray(realm.Intrinsics.ArrayPrototype);
        for (var i = 0; i < groupCount; i++)
        {
            array.Push(GetCapture(input, captures, i));
        }

        array.CreateDataPropertyOrThrow(IndexKey, captures[0]);
        array.CreateDataPropertyOrThrow(InputKey, input);

        JsObject? groups = null;
        if (program.HasNamedGroups)
        {
            groups = new JsObject(prototype: null);
            var matchedNames = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 1; i < groupCount; i++)
            {
                var name = program.GroupNames[i];
                if (name is null || matchedNames.Contains(name))
                    continue;

                var value = GetCapture(input, captures, i);
                if (!value.IsUndefined)
                {
                    matchedNames.Add(name);
                }

                groups.CreateDataPropertyOrThrow(name, value);
            }
        }

        array.CreateDataPropertyOrThrow(GroupsKey, groups is null ? JsValue.Undefined : groups);

        if ((regexp.RecordFlags & RegExpFlags.HasIndices) != 0)
        {
            array.CreateDataPropertyOrThrow(IndicesKey, MakeMatchIndicesIndexPairArray(realm, program, captures));
        }

        return array;
    }

    /// <summary>MakeMatchIndicesIndexPairArray</summary>
    private static JsArray MakeMatchIndicesIndexPairArray(JsRealm realm, RegExpProgram program, int[] captures)
    {
        var groupCount = captures.Length / 2;
        var array = new JsArray(realm.Intrinsics.ArrayPrototype);
        var groups = program.HasNamedGroups ? new JsObject(prototype: null) : null;
        array.CreateDataPropertyOrThrow(GroupsKey, groups is null ? JsValue.Undefined : groups);
        HashSet<string>? matchedNames = groups is null ? null : new(StringComparer.Ordinal);
        for (var i = 0; i < groupCount; i++)
        {
            var start = captures[i * 2];
            var end = captures[(i * 2) + 1];
            JsValue pair = start < 0 || end < 0 ? JsValue.Undefined : JsArray.Create(realm, [start, end]);
            array.Push(pair);
            if (i > 0 && groups is not null && program.GroupNames[i] is { } name && !matchedNames!.Contains(name))
            {
                if (!pair.IsUndefined)
                {
                    matchedNames.Add(name);
                }

                groups.CreateDataPropertyOrThrow(name, pair);
            }
        }

        return array;
    }

    private static JsValue GetCapture(string input, int[] captures, int index)
    {
        var start = captures[index * 2];
        var end = captures[(index * 2) + 1];
        if (start < 0 || end < 0)
            return JsValue.Undefined;

        return input.Substring(start, end - start);
    }

    /// <summary>RegExpExec(R, S)</summary>
    public static JsValue RegExpExec(JsObject regexp, string input)
    {
        if (TryGetBuiltinExec(regexp, out var builtin))
            return BuiltinExec(builtin.Realm, builtin, input);

        var exec = regexp.Get(ExecKey);
        if (exec.IsCallable)
        {
            var result = exec.AsObject().Call(regexp, [input]);
            if (!result.IsObject && !result.IsNull)
                return ThrowHelper.ThrowTypeError<JsValue>("The result of exec must be an object or null");

            return result;
        }

        if (regexp is not JsRegExpObject { Matcher: not null } regexpObject)
            return ThrowHelper.ThrowTypeError<JsValue>("RegExp exec method called on an incompatible receiver");

        return BuiltinExec(CurrentRealm, regexpObject, input);
    }

    /// <summary>
    /// Gets whether RegExpExec(R, S) would call the intrinsic exec of the current realm: the "exec" property of R is the
    /// one of %RegExp.prototype%, reached without running code. The match can then skip the creation of the result
    /// array.
    /// </summary>
    private static bool TryGetBuiltinExec(JsObject regexp, out JsRegExpObject builtin)
    {
        builtin = null!;
        if (regexp is not JsRegExpObject { Matcher: not null } candidate || !ReferenceEquals(candidate.Realm, CurrentRealm))
            return false;

        var intrinsics = candidate.Realm.Intrinsics;
        if (!ReferenceEquals(candidate.PrototypeSlot, intrinsics.RegExpPrototype) || candidate.GetOwnProperty(ExecKey, out _))
            return false;

        if (!intrinsics.RegExpPrototype.GetOwnProperty(ExecKey, out var descriptor) || !descriptor.IsDataDescriptor || descriptor.Value.Cell != intrinsics.RegExpPrototypeExec)
            return false;

        builtin = candidate;
        return true;
    }

    /// <summary>RegExpExec(R, S) when the intrinsic exec runs: the capture slots, or null; otherwise the result object.</summary>
    private static (int[]? Captures, JsValue Result) RegExpExecRaw(JsObject regexp, string input)
    {
        if (TryGetBuiltinExec(regexp, out var builtin))
        {
            var captures = BuiltinExecCore(builtin.Realm, builtin, input);
            return (captures, captures is null ? JsValue.Null : default);
        }

        return (null, RegExpExec(regexp, input));
    }

    /// <summary>AdvanceStringIndex(S, index, unicode)</summary>
    public static double AdvanceStringIndex(string input, double index, bool unicode)
    {
        if (!unicode || index + 1 >= input.Length)
            return index + 1;

        var i = (int)index;
        return char.IsHighSurrogate(input[i]) && char.IsLowSurrogate(input[i + 1]) ? index + 2 : index + 1;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // RegExp.prototype
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>RegExpHasFlag(R, codeUnit)</summary>
    private static JsValue HasFlag(JsBuiltinFunction function, JsValue thisValue, char flag)
    {
        if (thisValue.Cell is not JsObject obj)
            return ThrowHelper.ThrowTypeError<JsValue>("RegExp flag getter called on a non-object");

        if (obj is not JsRegExpObject { Matcher: not null } regexp)
        {
            if (ReferenceEquals(obj, function.Realm.Intrinsics.RegExpPrototype))
                return JsValue.Undefined;

            return ThrowHelper.ThrowTypeError<JsValue>("RegExp flag getter called on an incompatible receiver");
        }

        return (regexp.RecordFlags & RegExpFlagsExtensions.FromChar(flag)) != 0;
    }

    /// <summary>get RegExp.prototype.flags</summary>
    private static JsValue GetFlags(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is not JsObject regexp)
            return ThrowHelper.ThrowTypeError<JsValue>("RegExp.prototype.flags getter called on a non-object");

        Span<char> result = stackalloc char[8];
        var length = 0;
        foreach (var (name, flag) in (ReadOnlySpan<(string, char)>)[("hasIndices", 'd'), ("global", 'g'), ("ignoreCase", 'i'), ("multiline", 'm'), ("dotAll", 's'), ("unicode", 'u'), ("unicodeSets", 'v'), ("sticky", 'y')])
        {
            if (JsOperations.ToBoolean(regexp.Get(name)))
            {
                result[length++] = flag;
            }
        }

        return new string(result[..length]);
    }

    /// <summary>get RegExp.prototype.source</summary>
    private static JsValue GetSource(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is not JsObject obj)
            return ThrowHelper.ThrowTypeError<JsValue>("RegExp.prototype.source getter called on a non-object");

        if (obj is not JsRegExpObject { Matcher: not null } regexp)
        {
            if (ReferenceEquals(obj, function.Realm.Intrinsics.RegExpPrototype))
                return "(?:)";

            return ThrowHelper.ThrowTypeError<JsValue>("RegExp.prototype.source getter called on an incompatible receiver");
        }

        return EscapeRegExpPattern(regexp.OriginalSource, regexp.RecordFlags);
    }

    /// <summary>EscapeRegExpPattern(P, F): escapes the slashes and the line terminators, so that "/" + P + "/" + F is a literal.</summary>
    public static string EscapeRegExpPattern(string pattern, RegExpFlags flags)
    {
        if (pattern.Length == 0)
            return "(?:)";

        if (pattern.AsSpan().IndexOfAny("/\n\r\u2028\u2029") < 0)
            return pattern;

        var unicodeSets = (flags & RegExpFlags.UnicodeSets) != 0;
        var builder = new StringBuilder(pattern.Length + 8);
        var classDepth = 0;
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            switch (c)
            {
                case '\\':
                    builder.Append(c);
                    if (i + 1 < pattern.Length)
                    {
                        i++;
                        AppendEscaped(builder, pattern[i], afterBackslash: true);
                    }

                    break;

                case '[':
                    if (classDepth == 0 || unicodeSets)
                    {
                        classDepth++;
                    }

                    builder.Append(c);
                    break;

                case ']':
                    if (classDepth > 0)
                    {
                        classDepth--;
                    }

                    builder.Append(c);
                    break;

                case '/' when classDepth == 0:
                    builder.Append("\\/");
                    break;

                default:
                    AppendEscaped(builder, c, afterBackslash: false);
                    break;
            }
        }

        return builder.ToString();

        static void AppendEscaped(StringBuilder builder, char c, bool afterBackslash)
        {
            var escape = c switch
            {
                '\n' => "n",
                '\r' => "r",
                '\u2028' => "u2028",
                '\u2029' => "u2029",
                _ => null,
            };

            if (escape is null)
            {
                builder.Append(c);
            }
            else
            {
                if (!afterBackslash)
                {
                    builder.Append('\\');
                }

                builder.Append(escape);
            }
        }
    }

    /// <summary>RegExp.prototype.test(S)</summary>
    private static JsValue Test(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is not JsObject regexp)
            return ThrowHelper.ThrowTypeError<JsValue>("RegExp.prototype.test called on a non-object");

        var input = JsOperations.ToString(arguments.Length > 0 ? arguments[0] : default);
        var (captures, result) = RegExpExecRaw(regexp, input);
        return captures is not null || result.IsObject;
    }

    /// <summary>RegExp.prototype.toString()</summary>
    private static JsValue ToString(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is not JsObject regexp)
            return ThrowHelper.ThrowTypeError<JsValue>("RegExp.prototype.toString called on a non-object");

        var pattern = JsOperations.ToString(regexp.Get(SourceKey));
        var flags = JsOperations.ToString(regexp.Get(FlagsKey));
        return "/" + pattern + "/" + flags;
    }

    /// <summary>RegExp.prototype.compile(pattern, flags) (Annex B, with the checks of the legacy RegExp features)</summary>
    private static JsValue Compile(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is not JsRegExpObject { Matcher: not null } regexp)
            return ThrowHelper.ThrowTypeError<JsValue>("RegExp.prototype.compile called on an incompatible receiver");

        if (!ReferenceEquals(CurrentRealm, regexp.Realm))
            return ThrowHelper.ThrowTypeError<JsValue>("RegExp.prototype.compile called on a RegExp of another realm");

        if (!regexp.LegacyFeaturesEnabled)
            return ThrowHelper.ThrowTypeError<JsValue>("RegExp.prototype.compile called on an instance of a subclass of RegExp");

        var pattern = arguments.Length > 0 ? arguments[0] : default;
        var flags = arguments.Length > 1 ? arguments[1] : default;
        if (pattern.Cell is JsRegExpObject source)
        {
            if (!flags.IsUndefined)
                return ThrowHelper.ThrowTypeError<JsValue>("Cannot supply flags when constructing one RegExp from another");

            return Initialize(regexp, source.OriginalSource, source.OriginalFlags);
        }

        return Initialize(regexp, pattern, flags);
    }

    /// <summary>RegExp.prototype[%Symbol.match%](string)</summary>
    private static JsValue SymbolMatch(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is not JsObject regexp)
            return ThrowHelper.ThrowTypeError<JsValue>("RegExp.prototype[Symbol.match] called on a non-object");

        var input = JsOperations.ToString(arguments.Length > 0 ? arguments[0] : default);
        var flags = JsOperations.ToString(regexp.Get(FlagsKey));
        if (!flags.Contains('g', StringComparison.Ordinal))
            return RegExpExec(regexp, input);

        var fullUnicode = flags.Contains('u', StringComparison.Ordinal) || flags.Contains('v', StringComparison.Ordinal);
        regexp.Set(LastIndexKey, 0, throwOnFailure: true);
        var array = new JsArray(function.Realm.Intrinsics.ArrayPrototype);
        while (true)
        {
            var (captures, result) = RegExpExecRaw(regexp, input);
            string matched;
            if (captures is not null)
            {
                matched = input[captures[0]..captures[1]];
            }
            else if (result.Cell is JsObject resultObject)
            {
                matched = JsOperations.ToString(resultObject.Get(ZeroKey));
            }
            else
            {
                return array.Length == 0 ? JsValue.Null : array;
            }

            array.Push(matched);
            if (matched.Length == 0)
            {
                var thisIndex = JsOperations.ToLength(regexp.Get(LastIndexKey));
                regexp.Set(LastIndexKey, AdvanceStringIndex(input, thisIndex, fullUnicode), throwOnFailure: true);
            }
        }
    }

    /// <summary>RegExp.prototype[%Symbol.matchAll%](string)</summary>
    private static JsValue SymbolMatchAll(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is not JsObject regexp)
            return ThrowHelper.ThrowTypeError<JsValue>("RegExp.prototype[Symbol.matchAll] called on a non-object");

        var input = JsOperations.ToString(arguments.Length > 0 ? arguments[0] : default);
        var intrinsics = function.Realm.Intrinsics;
        var constructor = ObjectOperations.SpeciesConstructor(regexp, intrinsics.RegExp);
        var flags = JsOperations.ToString(regexp.Get(FlagsKey));
        var matcher = JsOperations.Construct(constructor, [regexp, flags]).AsObject();
        var lastIndex = JsOperations.ToLength(regexp.Get(LastIndexKey));
        matcher.Set(LastIndexKey, lastIndex, throwOnFailure: true);
        var global = flags.Contains('g', StringComparison.Ordinal);
        var fullUnicode = flags.Contains('u', StringComparison.Ordinal) || flags.Contains('v', StringComparison.Ordinal);
        return new JsRegExpStringIterator(intrinsics.RegExpStringIteratorPrototype, matcher, input, global, fullUnicode);
    }

    /// <summary>%RegExpStringIteratorPrototype%.next()</summary>
    private static JsValue IteratorNext(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is not JsRegExpStringIterator iterator)
            return ThrowHelper.ThrowTypeError<JsValue>("%RegExpStringIteratorPrototype%.next called on an incompatible receiver");

        var realm = function.Realm;
        if (iterator.Done)
            return IteratorOperations.CreateIteratorResultObject(realm, JsValue.Undefined, done: true);

        var match = RegExpExec(iterator.IteratingRegExp, iterator.IteratedString);
        if (match.IsNull)
        {
            iterator.Done = true;
            return IteratorOperations.CreateIteratorResultObject(realm, JsValue.Undefined, done: true);
        }

        if (!iterator.Global)
        {
            iterator.Done = true;
            return IteratorOperations.CreateIteratorResultObject(realm, match, done: false);
        }

        var matched = JsOperations.ToString(match.AsObject().Get(ZeroKey));
        if (matched.Length == 0)
        {
            var regexp = iterator.IteratingRegExp;
            var thisIndex = JsOperations.ToLength(regexp.Get(LastIndexKey));
            regexp.Set(LastIndexKey, AdvanceStringIndex(iterator.IteratedString, thisIndex, iterator.FullUnicode), throwOnFailure: true);
        }

        return IteratorOperations.CreateIteratorResultObject(realm, match, done: false);
    }

    /// <summary>RegExp.prototype[%Symbol.replace%](string, replaceValue)</summary>
    private static JsValue SymbolReplace(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is not JsObject regexp)
            return ThrowHelper.ThrowTypeError<JsValue>("RegExp.prototype[Symbol.replace] called on a non-object");

        var input = JsOperations.ToString(arguments.Length > 0 ? arguments[0] : default);
        var length = input.Length;
        var replaceValue = arguments.Length > 1 ? arguments[1] : default;
        var functionalReplace = replaceValue.IsCallable;
        string? replaceTemplate = null;
        if (!functionalReplace)
        {
            replaceTemplate = JsOperations.ToString(replaceValue);
        }

        var flags = JsOperations.ToString(regexp.Get(FlagsKey));
        var global = flags.Contains('g', StringComparison.Ordinal);
        var fullUnicode = flags.Contains('u', StringComparison.Ordinal) || flags.Contains('v', StringComparison.Ordinal);
        if (global)
        {
            regexp.Set(LastIndexKey, 0, throwOnFailure: true);
        }

        // The results: the capture slots of the intrinsic exec, or the objects of a custom exec
        var results = new List<(int[]? Captures, JsObject? Object)>();
        while (true)
        {
            var (captures, result) = RegExpExecRaw(regexp, input);
            if (captures is null && result.Cell is not JsObject)
                break;

            var resultObject = result.Cell as JsObject;
            results.Add((captures, resultObject));
            if (!global)
                break;

            var matchedLength = captures is not null ? captures[1] - captures[0] : JsOperations.ToString(resultObject!.Get(ZeroKey)).Length;
            if (matchedLength == 0)
            {
                var thisIndex = JsOperations.ToLength(regexp.Get(LastIndexKey));
                regexp.Set(LastIndexKey, AdvanceStringIndex(input, thisIndex, fullUnicode), throwOnFailure: true);
            }
        }

        var builder = new StringBuilder();
        var nextSourcePosition = 0;
        foreach (var (captures, resultObject) in results)
        {
            string matched;
            int position;
            JsValue[] captureValues;
            JsValue namedCaptures;
            if (captures is not null)
            {
                matched = input[captures[0]..captures[1]];
                position = captures[0];
                captureValues = new JsValue[(captures.Length / 2) - 1];
                for (var i = 0; i < captureValues.Length; i++)
                {
                    captureValues[i] = GetCapture(input, captures, i + 1);
                }

                // The groups object is only read by the replacer function and by "$<"
                var program = ((JsRegExpObject)regexp).Matcher!;
                namedCaptures = program.HasNamedGroups && (functionalReplace || replaceTemplate!.Contains("$<", StringComparison.Ordinal))
                    ? CreateGroups(program, input, captures)
                    : JsValue.Undefined;
            }
            else
            {
                var result = resultObject!;
                var resultLength = JsOperations.LengthOfArrayLike(result);
                var capturesCount = Math.Max(resultLength - 1, 0);
                matched = JsOperations.ToString(result.Get(ZeroKey));
                var positionValue = JsOperations.ToIntegerOrInfinity(result.Get(IndexKey));
                position = (int)Math.Clamp(positionValue, 0, length);
                captureValues = new JsValue[capturesCount];
                for (var i = 0; i < capturesCount; i++)
                {
                    var capture = result.Get(PropertyKey.FromInteger(i + 1));
                    captureValues[i] = capture.IsUndefined ? capture : JsOperations.ToString(capture);
                }

                namedCaptures = result.Get(GroupsKey);
            }

            string replacement;
            if (functionalReplace)
            {
                var replacerArguments = new List<JsValue>(captureValues.Length + 4) { matched };
                replacerArguments.AddRange(captureValues);
                replacerArguments.Add(position);
                replacerArguments.Add(input);
                if (!namedCaptures.IsUndefined)
                {
                    replacerArguments.Add(namedCaptures);
                }

                replacement = JsOperations.ToString(JsOperations.Call(replaceValue, JsValue.Undefined, [.. replacerArguments]));
            }
            else
            {
                var namedObject = namedCaptures.IsUndefined ? null : JsOperations.ToObject(namedCaptures);
                replacement = GetSubstitution(matched, input, position, captureValues, namedObject, replaceTemplate!);
            }

            if (position >= nextSourcePosition)
            {
                StringOperations.CheckLength((long)builder.Length + (position - nextSourcePosition) + replacement.Length);
                builder.Append(input, nextSourcePosition, position - nextSourcePosition).Append(replacement);
                nextSourcePosition = position + matched.Length;
            }
        }

        if (nextSourcePosition >= length)
            return builder.ToString();

        StringOperations.CheckLength((long)builder.Length + (length - nextSourcePosition));
        return builder.Append(input, nextSourcePosition, length - nextSourcePosition).ToString();
    }

    private static JsObject CreateGroups(RegExpProgram program, string input, int[] captures)
    {
        var groups = new JsObject(prototype: null);
        var matchedNames = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 1; i < captures.Length / 2; i++)
        {
            var name = program.GroupNames[i];
            if (name is null || matchedNames.Contains(name))
                continue;

            var value = GetCapture(input, captures, i);
            if (!value.IsUndefined)
            {
                matchedNames.Add(name);
            }

            groups.CreateDataPropertyOrThrow(name, value);
        }

        return groups;
    }

    /// <summary>GetSubstitution(matched, str, position, captures, namedCaptures, replacementTemplate)</summary>
    public static string GetSubstitution(string matched, string input, int position, ReadOnlySpan<JsValue> captures, JsObject? namedCaptures, string template)
    {
        var dollar = template.IndexOf('$', StringComparison.Ordinal);
        if (dollar < 0)
            return template;

        var builder = new StringBuilder(template.Length + matched.Length);
        builder.Append(template, 0, dollar);
        var i = dollar;
        while (i < template.Length)
        {
            var c = template[i];
            if (c != '$' || i + 1 >= template.Length)
            {
                builder.Append(c);
                i++;
                continue;
            }

            var next = template[i + 1];
            switch (next)
            {
                case '$':
                    builder.Append('$');
                    i += 2;
                    break;

                case '`':
                    AppendChecked(builder, input.AsSpan(0, position));
                    i += 2;
                    break;

                case '&':
                    AppendChecked(builder, matched);
                    i += 2;
                    break;

                case '\'':
                    {
                        var tailPosition = Math.Min(position + matched.Length, input.Length);
                        AppendChecked(builder, input.AsSpan(tailPosition));
                        i += 2;
                        break;
                    }

                case >= '0' and <= '9':
                    {
                        var digitCount = i + 2 < template.Length && char.IsAsciiDigit(template[i + 2]) ? 2 : 1;
                        var index = digitCount == 2 ? ((next - '0') * 10) + (template[i + 2] - '0') : next - '0';
                        if (index > captures.Length && digitCount == 2)
                        {
                            digitCount = 1;
                            index = next - '0';
                        }

                        if (index >= 1 && index <= captures.Length)
                        {
                            var capture = captures[index - 1];
                            if (!capture.IsUndefined)
                            {
                                AppendChecked(builder, capture.AsString());
                            }
                        }
                        else
                        {
                            builder.Append(template, i, 1 + digitCount);
                        }

                        i += 1 + digitCount;
                        break;
                    }

                case '<':
                    {
                        var greaterThan = template.IndexOf('>', i, StringComparison.Ordinal);
                        if (greaterThan < 0 || namedCaptures is null)
                        {
                            builder.Append("$<");
                            i += 2;
                            break;
                        }

                        var groupName = template[(i + 2)..greaterThan];
                        var capture = namedCaptures.Get(groupName);
                        if (!capture.IsUndefined)
                        {
                            AppendChecked(builder, JsOperations.ToString(capture));
                        }

                        i = greaterThan + 1;
                        break;
                    }

                default:
                    builder.Append('$');
                    i++;
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>Appends a part of a replacement, or throws a RangeError when the string would be too long.</summary>
    private static void AppendChecked(StringBuilder builder, ReadOnlySpan<char> value)
    {
        StringOperations.CheckLength((long)builder.Length + value.Length);
        builder.Append(value);
    }

    /// <summary>RegExp.prototype[%Symbol.search%](string)</summary>
    private static JsValue SymbolSearch(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is not JsObject regexp)
            return ThrowHelper.ThrowTypeError<JsValue>("RegExp.prototype[Symbol.search] called on a non-object");

        var input = JsOperations.ToString(arguments.Length > 0 ? arguments[0] : default);
        var previousLastIndex = regexp.Get(LastIndexKey);
        if (!JsOperations.SameValue(previousLastIndex, 0))
        {
            regexp.Set(LastIndexKey, 0, throwOnFailure: true);
        }

        var (captures, result) = RegExpExecRaw(regexp, input);
        var currentLastIndex = regexp.Get(LastIndexKey);
        if (!JsOperations.SameValue(currentLastIndex, previousLastIndex))
        {
            regexp.Set(LastIndexKey, previousLastIndex, throwOnFailure: true);
        }

        if (captures is not null)
            return captures[0];

        if (result.Cell is not JsObject resultObject)
            return -1;

        return resultObject.Get(IndexKey);
    }

    /// <summary>RegExp.prototype[%Symbol.split%](string, limit)</summary>
    private static JsValue SymbolSplit(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is not JsObject regexp)
            return ThrowHelper.ThrowTypeError<JsValue>("RegExp.prototype[Symbol.split] called on a non-object");

        var input = JsOperations.ToString(arguments.Length > 0 ? arguments[0] : default);
        var limit = arguments.Length > 1 ? arguments[1] : default;
        var realm = function.Realm;
        var constructor = ObjectOperations.SpeciesConstructor(regexp, realm.Intrinsics.RegExp);
        var flags = JsOperations.ToString(regexp.Get(FlagsKey));
        var unicodeMatching = flags.Contains('u', StringComparison.Ordinal) || flags.Contains('v', StringComparison.Ordinal);
        var newFlags = flags.Contains('y', StringComparison.Ordinal) ? flags : flags + "y";
        var splitter = JsOperations.Construct(constructor, [regexp, newFlags]).AsObject();
        var array = new JsArray(realm.Intrinsics.ArrayPrototype);
        var lim = limit.IsUndefined ? uint.MaxValue : JsOperations.ToUint32(limit);
        if (lim == 0)
            return array;

        var size = input.Length;
        if (size == 0)
        {
            var (captures, result) = RegExpExecRaw(splitter, input);
            if (captures is null && result.IsNull)
            {
                array.Push(input);
            }

            return array;
        }

        // When the splitter is a RegExp of this realm that runs the intrinsic exec, nothing observes the sticky attempts
        // at each position: a single search finds the next position where a sticky match succeeds
        JsRegExpObject? builtinSplitter = null;
        if (ReferenceEquals(constructor, realm.Intrinsics.RegExp) && TryGetBuiltinExec(splitter, out var candidate))
        {
            builtinSplitter = candidate;
        }

        var lastMatchEnd = 0;
        var searchIndex = 0;
        while (searchIndex < size)
        {
            int[]? captures;
            JsValue result = default;
            if (builtinSplitter is not null)
            {
                captures = builtinSplitter.Matcher!.Exec(input, searchIndex, sticky: false, JsEngine.Current);
                if (captures is null || captures[0] >= size)
                    break;

                searchIndex = captures[0];
                UpdateLegacyStaticProperties(builtinSplitter.Realm, builtinSplitter, input, captures);
            }
            else
            {
                splitter.Set(LastIndexKey, searchIndex, throwOnFailure: true);
                (captures, result) = RegExpExecRaw(splitter, input);
                if (captures is null && result.IsNull)
                {
                    searchIndex = (int)AdvanceStringIndex(input, searchIndex, unicodeMatching);
                    continue;
                }
            }

            var matchEnd = captures is not null ? Math.Min(captures[1], size) : (int)Math.Min(JsOperations.ToLength(splitter.Get(LastIndexKey)), size);
            if (matchEnd == lastMatchEnd)
            {
                searchIndex = (int)AdvanceStringIndex(input, searchIndex, unicodeMatching);
                continue;
            }

            array.Push(input[lastMatchEnd..searchIndex]);
            if (array.Length == lim)
                return array;

            lastMatchEnd = matchEnd;
            if (captures is not null)
            {
                for (var i = 1; i < captures.Length / 2; i++)
                {
                    array.Push(GetCapture(input, captures, i));
                    if (array.Length == lim)
                        return array;
                }
            }
            else
            {
                var resultObject = result.AsObject();
                var numberOfCaptures = Math.Max(JsOperations.LengthOfArrayLike(resultObject) - 1, 0);
                for (var i = 1; i <= numberOfCaptures; i++)
                {
                    array.Push(resultObject.Get(PropertyKey.FromInteger(i)));
                    if (array.Length == lim)
                        return array;
                }
            }

            searchIndex = lastMatchEnd;
        }

        array.Push(input[lastMatchEnd..]);
        return array;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Legacy static properties (https://github.com/tc39/proposal-regexp-legacy-features)
    // ------------------------------------------------------------------------------------------------------------------

    private static void DefineLegacyAccessors(JsRealm realm, JsObject constructor)
    {
        foreach (var name in (ReadOnlySpan<string>)["input", "$_"])
        {
            realm.DefineAccessor(constructor, name, GetLegacyInput, SetLegacyInput);
        }

        foreach (var name in (ReadOnlySpan<string>)["lastMatch", "$&"])
        {
            realm.DefineAccessor(constructor, name, (f, t, _, _) => GetLegacy(f, t, s => s.LastMatch), setter: null);
        }

        foreach (var name in (ReadOnlySpan<string>)["lastParen", "$+"])
        {
            realm.DefineAccessor(constructor, name, (f, t, _, _) => GetLegacy(f, t, s => s.LastParen), setter: null);
        }

        foreach (var name in (ReadOnlySpan<string>)["leftContext", "$`"])
        {
            realm.DefineAccessor(constructor, name, (f, t, _, _) => GetLegacy(f, t, s => s.LeftContext), setter: null);
        }

        foreach (var name in (ReadOnlySpan<string>)["rightContext", "$'"])
        {
            realm.DefineAccessor(constructor, name, (f, t, _, _) => GetLegacy(f, t, s => s.RightContext), setter: null);
        }

        for (var i = 1; i <= 9; i++)
        {
            var index = i;
            realm.DefineAccessor(constructor, "$" + i.ToString(CultureInfo.InvariantCulture), (f, t, _, _) => GetLegacy(f, t, s => s.GetParen(index)), setter: null);
        }
    }

    /// <summary>GetLegacyRegExpStaticProperty(%RegExp%, thisValue, internalSlotName)</summary>
    private static JsValue GetLegacy(JsBuiltinFunction function, JsValue thisValue, Func<RegExpLegacyStaticProperties, string?> slot)
    {
        var intrinsics = function.Realm.Intrinsics;
        if (thisValue.Cell != intrinsics.RegExp)
            return ThrowHelper.ThrowTypeError<JsValue>("The legacy RegExp static properties are only available on RegExp itself");

        var value = slot(intrinsics.RegExpLegacyStatics);
        if (value is null)
            return ThrowHelper.ThrowTypeError<JsValue>("The legacy RegExp static properties are not available after a match of a RegExp subclass");

        return value;
    }

    private static JsValue GetLegacyInput(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return GetLegacy(function, thisValue, s => s.Input);
    }

    /// <summary>SetLegacyRegExpStaticProperty(%RegExp%, thisValue, [[RegExpInput]], val)</summary>
    private static JsValue SetLegacyInput(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var intrinsics = function.Realm.Intrinsics;
        if (thisValue.Cell != intrinsics.RegExp)
            return ThrowHelper.ThrowTypeError<JsValue>("The legacy RegExp static properties are only available on RegExp itself");

        intrinsics.RegExpLegacyStatics.Input = JsOperations.ToString(arguments.Length > 0 ? arguments[0] : default);
        return JsValue.Undefined;
    }
}

/// <summary>A RegExp String Iterator (https://tc39.es/ecma262/#sec-regexp-string-iterator-objects).</summary>
internal sealed class JsRegExpStringIterator : JsObject
{
    public JsRegExpStringIterator(JsObject prototype, JsObject regexp, string input, bool global, bool fullUnicode)
        : base(prototype)
    {
        IteratingRegExp = regexp;
        IteratedString = input;
        Global = global;
        FullUnicode = fullUnicode;
    }

    /// <summary>[[IteratingRegExp]]</summary>
    public JsObject IteratingRegExp { get; }

    /// <summary>[[IteratedString]]</summary>
    public string IteratedString { get; }

    /// <summary>[[Global]]</summary>
    public bool Global { get; }

    /// <summary>[[Unicode]]</summary>
    public bool FullUnicode { get; }

    /// <summary>[[Done]]</summary>
    public bool Done { get; set; }
}
