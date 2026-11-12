using Meziantou.Framework.WebPlatform.WebAssembly;

namespace Meziantou.Framework.WebPlatform.Scripting;

// The JS String Builtins (https://webassembly.github.io/spec/js-api/#builtins): the builtin set "js-string", imported from
// the "wasm:js-string" module, and the imported string constants (the importedStringConstants compile option).
public sealed partial class WebIdlRealm<TAdapter, TValue>
{
    /// <summary>The name of the builtin set of the JS String Builtins.</summary>
    private const string JsStringBuiltinSetName = "js-string";

    /// <summary>The module of the imports of the builtin set "js-string" ("wasm:" followed by the name of the set).</summary>
    private const string JsStringBuiltinModule = "wasm:js-string";

    /// <summary>The builtins of the set "js-string" (https://webassembly.github.io/spec/js-api/#js-string-builtin-functions).</summary>
    private enum JsStringBuiltin
    {
        Cast,
        Test,
        FromCharCodeArray,
        IntoCharCodeArray,
        FromCharCode,
        FromCodePoint,
        CharCodeAt,
        CodePointAt,
        Length,
        Concat,
        Substring,
        EqualsBuiltin,
        Compare,
    }

    private static readonly WasmValueType RefExtern = new(WasmValueKind.Ref, WasmHeapKind.Extern, Nullable: false);

    /// <summary>
    /// https://webassembly.github.io/spec/js-api/#find-a-builtin: the builtin of the import of a module whose builtin sets are
    /// <paramref name="builtinSets"/>, or null when the import is not a builtin.
    /// </summary>
    private static JsStringBuiltin? FindWasmBuiltin(WasmImport import, IReadOnlyList<string> builtinSets)
    {
        if (import.Module != JsStringBuiltinModule || !builtinSets.Contains(JsStringBuiltinSetName, StringComparer.Ordinal))
            return null;

        return import.Name switch
        {
            "cast" => JsStringBuiltin.Cast,
            "test" => JsStringBuiltin.Test,
            "fromCharCodeArray" => JsStringBuiltin.FromCharCodeArray,
            "intoCharCodeArray" => JsStringBuiltin.IntoCharCodeArray,
            "fromCharCode" => JsStringBuiltin.FromCharCode,
            "fromCodePoint" => JsStringBuiltin.FromCodePoint,
            "charCodeAt" => JsStringBuiltin.CharCodeAt,
            "codePointAt" => JsStringBuiltin.CodePointAt,
            "length" => JsStringBuiltin.Length,
            "concat" => JsStringBuiltin.Concat,
            "substring" => JsStringBuiltin.Substring,
            "equals" => JsStringBuiltin.EqualsBuiltin,
            "compare" => JsStringBuiltin.Compare,
            _ => null,
        };
    }

    /// <summary>
    /// Whether the import of a module is provided by the host (https://webassembly.github.io/spec/js-api/#read-the-imports):
    /// its module is the one of an enabled builtin set, or the module of the imported string constants.
    /// </summary>
    internal static bool IsWasmBuiltinOrStringImport(WasmImport import, IReadOnlyList<string> builtinSets, string? importedStringModule)
        => (import.Module == JsStringBuiltinModule && builtinSets.Contains(JsStringBuiltinSetName, StringComparer.Ordinal)) || (importedStringModule is not null && import.Module == importedStringModule);

    /// <summary>
    /// Whether <c>WebAssembly.Module.imports()</c> skips an import (https://webassembly.github.io/spec/js-api/#dom-module-imports):
    /// a builtin of an enabled builtin set, or an imported string constant.
    /// </summary>
    internal static bool IsReflectedAsWasmBuiltinOrStringImport(WasmImport import, IReadOnlyList<string> builtinSets, string? importedStringModule)
        => FindWasmBuiltin(import, builtinSets) is not null || (importedStringModule is not null && import.Module == importedStringModule);

    /// <summary>
    /// https://webassembly.github.io/spec/js-api/#validate-builtins-and-imported-string-for-a-webassembly-module: the builtin
    /// set names have no duplicates, an import of a builtin has the type of the builtin, and an imported string constant is an
    /// immutable global whose type is a supertype of <c>(ref extern)</c>.
    /// </summary>
    internal static bool ValidateWasmBuiltinsAndImportedStrings(WasmModule module, IReadOnlyList<string> builtinSets, string? importedStringModule)
    {
        if (builtinSets.Distinct(StringComparer.Ordinal).Count() != builtinSets.Count)
            return false;

        foreach (var import in module.Imports)
        {
            if (importedStringModule is not null && import.Module == importedStringModule)
            {
                if (import.Type is not WasmGlobalType { Mutable: false, ValueType: { Kind: WasmValueKind.Ref, Heap: WasmHeapKind.Extern } })
                    return false;

                continue;
            }

            if (FindWasmBuiltin(import, builtinSets) is { } builtin && !MatchesWasmBuiltinType(import.Type, builtin))
                return false;
        }

        return true;
    }

    /// <summary>
    /// match_externtype(builtinExternType, importExternType): the function types are matched by equality. The array of
    /// <c>fromCharCodeArray</c> and <c>intoCharCodeArray</c> must be a nullable reference to a concrete array type: its
    /// element type, <c>(mut i16)</c>, is checked by the runtime when the array is read or written.
    /// </summary>
    private static bool MatchesWasmBuiltinType(WasmExternType type, JsStringBuiltin builtin)
    {
        if (type is not WasmFunctionType function)
            return false;

        var i32 = WasmValueType.I32;
        var externRef = WasmValueType.ExternRef;
        var array = new WasmValueType(WasmValueKind.Ref, WasmHeapKind.ConcreteArray, Nullable: true);
        var (parameters, results) = builtin switch
        {
            JsStringBuiltin.Cast => ((WasmValueType[])[externRef], (WasmValueType[])[RefExtern]),
            JsStringBuiltin.Test => ([externRef], [i32]),
            JsStringBuiltin.FromCharCodeArray => ([array, i32, i32], [RefExtern]),
            JsStringBuiltin.IntoCharCodeArray => ([externRef, array, i32], [i32]),
            JsStringBuiltin.FromCharCode => ([i32], [RefExtern]),
            JsStringBuiltin.FromCodePoint => ([i32], [RefExtern]),
            JsStringBuiltin.CharCodeAt => ([externRef, i32], [i32]),
            JsStringBuiltin.CodePointAt => ([externRef, i32], [i32]),
            JsStringBuiltin.Length => ([externRef], [i32]),
            JsStringBuiltin.Concat => ([externRef, externRef], [RefExtern]),
            JsStringBuiltin.Substring => ([externRef, i32, i32], [RefExtern]),
            JsStringBuiltin.EqualsBuiltin => ([externRef, externRef], [i32]),
            _ => ([externRef, externRef], [i32]),
        };

        return function.Parameters.AsSpan().SequenceEqual(parameters) && function.Results.AsSpan().SequenceEqual(results);
    }

    /// <summary>
    /// The extern of an import provided by the host (https://webassembly.github.io/spec/js-api/#read-the-imports): a builtin
    /// function of the set "js-string" (https://webassembly.github.io/spec/js-api/#instantiate-a-builtin-set), or a string
    /// constant (https://webassembly.github.io/spec/js-api/#instantiate-imported-strings): an immutable global whose value
    /// is the name of the import.
    /// </summary>
    private WasmExtern ReadWasmBuiltinOrStringImport(WebAssemblyModuleObject moduleObject, WasmImport import)
    {
        var store = WebAssemblyState.Store;
        if (moduleObject.ImportedStringModule is not null && import.Module == moduleObject.ImportedStringModule && import.Type is WasmGlobalType globalType)
            return WasmCall(() => store.CreateGlobal(globalType, ToWasmValue(Adapter.FromString(import.Name), globalType.ValueType)));

        // An import of the module of the builtin set whose name is not a builtin is not a function of the builtin set
        if (FindWasmBuiltin(import, moduleObject.BuiltinSets) is not { } builtin || import.Type is not WasmFunctionType functionType)
            throw CreateWasmError("LinkError", $"The import '{import.Module}.{import.Name}' is not a function");

        return WasmCall(() => store.CreateHostFunction(functionType, hostFunction: null, (arguments, results) => results[0] = RunWasmBuiltin(builtin, arguments)));
    }

    /// <summary>The steps of a builtin of the set "js-string" (https://webassembly.github.io/spec/js-api/#js-string-builtin-functions).</summary>
    private WasmValue RunWasmBuiltin(JsStringBuiltin builtin, ReadOnlySpan<WasmValue> arguments)
    {
        switch (builtin)
        {
            case JsStringBuiltin.Cast:
                return ToWasmString(UnwrapWasmString(arguments[0]));

            case JsStringBuiltin.Test:
                return WasmValue.FromI32(Adapter.IsString(ToJsValue(arguments[0])) ? 1 : 0);

            case JsStringBuiltin.FromCharCodeArray:
            {
                if (arguments[0].Reference is not WasmGcObject array)
                    throw new WasmTrapException("The array is null");

                var store = WebAssemblyState.Store;
                var start = (uint)arguments[1].I32;
                var end = (uint)arguments[2].I32;
                if (start > end || end > (uint)store.GetArrayLength(array))
                    throw new WasmTrapException("The range is out of the bounds of the array");

                var chars = new char[end - start];
                for (var i = 0; i < chars.Length; i++)
                {
                    chars[i] = (char)store.GetArrayElement(array, (int)start + i).I32;
                }

                return ToWasmString(new string(chars));
            }

            case JsStringBuiltin.IntoCharCodeArray:
            {
                var value = UnwrapWasmString(arguments[0]);
                if (arguments[1].Reference is not WasmGcObject array)
                    throw new WasmTrapException("The array is null");

                var store = WebAssemblyState.Store;
                var start = (ulong)(uint)arguments[2].I32;
                if (start + (ulong)value.Length > (ulong)(uint)store.GetArrayLength(array))
                    throw new WasmTrapException("The range is out of the bounds of the array");

                for (var i = 0; i < value.Length; i++)
                {
                    store.SetArrayElement(array, (int)start + i, WasmValue.FromI32(value[i]), WasmValueType.I32);
                }

                return WasmValue.FromI32(value.Length);
            }

            case JsStringBuiltin.FromCharCode:
                return ToWasmString(((char)arguments[0].I32).ToString());

            case JsStringBuiltin.FromCodePoint:
            {
                var codePoint = (uint)arguments[0].I32;
                if (codePoint > 0x10FFFF)
                    throw new WasmTrapException("The code point is not valid");

                // String.fromCodePoint: a lone surrogate is a code unit
                return ToWasmString(codePoint <= 0xFFFF ? ((char)codePoint).ToString() : char.ConvertFromUtf32((int)codePoint));
            }

            case JsStringBuiltin.CharCodeAt:
            {
                var value = UnwrapWasmString(arguments[0]);
                var index = (uint)arguments[1].I32;
                if (index >= (uint)value.Length)
                    throw new WasmTrapException("The index is out of the bounds of the string");

                return WasmValue.FromI32(value[(int)index]);
            }

            case JsStringBuiltin.CodePointAt:
            {
                var value = UnwrapWasmString(arguments[0]);
                var index = (uint)arguments[1].I32;
                if (index >= (uint)value.Length)
                    throw new WasmTrapException("The index is out of the bounds of the string");

                var first = value[(int)index];
                return WasmValue.FromI32(char.IsHighSurrogate(first) && index + 1 < (uint)value.Length && char.IsLowSurrogate(value[(int)index + 1]) ? char.ConvertToUtf32(first, value[(int)index + 1]) : first);
            }

            case JsStringBuiltin.Length:
                return WasmValue.FromI32(UnwrapWasmString(arguments[0]).Length);

            case JsStringBuiltin.Concat:
            {
                var first = UnwrapWasmString(arguments[0]);
                var second = UnwrapWasmString(arguments[1]);
                return ToWasmString(first + second);
            }

            case JsStringBuiltin.Substring:
            {
                var value = UnwrapWasmString(arguments[0]);
                var start = (uint)arguments[1].I32;
                var end = (uint)arguments[2].I32;
                if (start > end || start > (uint)value.Length)
                    return ToWasmString("");

                return ToWasmString(value[(int)start..(int)Math.Min(end, (uint)value.Length)]);
            }

            case JsStringBuiltin.EqualsBuiltin:
            {
                var first = ToJsValue(arguments[0]);
                var second = ToJsValue(arguments[1]);
                if ((!Adapter.IsNull(first) && !Adapter.IsString(first)) || (!Adapter.IsNull(second) && !Adapter.IsString(second)))
                    throw new WasmTrapException("The value is not a string");

                var equal = Adapter.IsNull(first) ? Adapter.IsNull(second) : !Adapter.IsNull(second) && string.Equals(Adapter.AsString(first), Adapter.AsString(second), StringComparison.Ordinal);
                return WasmValue.FromI32(equal ? 1 : 0);
            }

            default:
            {
                var first = UnwrapWasmString(arguments[0]);
                var second = UnwrapWasmString(arguments[1]);
                return WasmValue.FromI32(Math.Sign(string.CompareOrdinal(first, second)));
            }
        }
    }

    /// <summary>https://webassembly.github.io/spec/js-api/#unwrapstring: the string of an <c>externref</c>, else a trap.</summary>
    private string UnwrapWasmString(WasmValue value)
    {
        var jsValue = ToJsValue(value);
        if (!Adapter.IsString(jsValue))
            throw new WasmTrapException("The value is not a string");

        return Adapter.AsString(jsValue);
    }

    /// <summary>A string as a <c>(ref extern)</c> value.</summary>
    private WasmValue ToWasmString(string value) => ToWasmValue(Adapter.FromString(value), RefExtern);
}
