# Architecture of Meziantou.Framework.JavaScriptEngine

The engine implements ECMAScript (ES2025 + Annex B, the finished proposals, Temporal and ECMA-402 Intl). It is validated
by test262 (`tests/Meziantou.Framework.JavaScriptEngine.Test262`).

## Pipeline

```
source text ──Parsing/JsParser──▶ Ast (Program, nodes) ──Compiler──▶ bytecode (FunctionTemplate) ──Interpreter──▶ values
```

| Folder | Content |
|---|---|
| `Text/` | `SourceText`: the text, its URL, the line map (ECMAScript line terminators) and the line/column offsets of inline scripts. |
| `Ast/` | The syntax tree: one sealed class per node (`Node.Start`/`End` are UTF-16 offsets), with the `NodeType` enum for fast dispatch. ESTree-like shapes. |
| `Parsing/` | `JsParser` (hand-written recursive descent, all early errors), `ParserOptions` (goal and eval context). Syntax errors throw `JsSyntaxErrorException`. |
| `Compiler/` | Scope analysis and the bytecode generator. |
| `Interpreter/` | The bytecode interpreter: frames on the heap, no .NET recursion for JavaScript-to-JavaScript calls. |
| `Runtime/` | The object model and the abstract operations shared by everything. |
| `Builtins/` | The built-in objects of the realm: one static class per built-in (e.g. `ArrayBuiltins`). |

## Values

`JsValue` is a 16-byte struct: a `JsCell? Cell` and a `double`. `default(JsValue)` is `undefined`; a number keeps its
value in the struct (no allocation) with `Cell == NumberCell.Instance`; every other value is the cell itself:
`NullCell`, `BooleanCell.True/False`, `JsString` (flat or rope), `JsSymbol`, `JsBigInt` (`BigInteger`), `JsObject`.
`JsValue.Hole` marks an uninitialized binding (TDZ) or an array hole; it must never escape to user code.

`JsValue.Kind` is a field read (`JsCell.Kind`). Prefer `value.IsNumber` + `value.NumberValue`, `value.Cell is JsString s`,
`value.TryGetObject()` in hot paths.

Strings: `JsString.Create(string)`, `JsString.Concat(a, b)` (ropes), `jsString.ToString()` flattens. Use
`JsValue.FromJsString(s)` to wrap one. A string longer than `JsString.MaxLength` must throw a RangeError (check with
`JsString.CanConcat`).

## Property keys and descriptors

`PropertyKey` is a struct: an array index (`uint` < 2^32-1, `IsArrayIndex`), a string or a `JsSymbol`. It has implicit
conversions from `string` and `JsSymbol`. Convert a value with `JsOperations.ToPropertyKey`.

`PropertyDescriptor` is the spec type: `Value`, `Get`, `Set`, `Flags` (`PropertyFlags.Writable|Enumerable|Configurable`)
and `Fields` (`DescriptorFields`: which fields are present). `PropertyDescriptor.Data(value, flags)` and
`PropertyDescriptor.Accessor(get, set, flags)` create complete descriptors.

## Objects

`JsObject` implements the ordinary internal methods; the exotic objects override them (all `internal virtual`):
`GetPrototypeOf`, `SetPrototypeOf`, `IsExtensible`, `PreventExtensions`, `GetOwnProperty`, `DefineOwnProperty`,
`HasProperty`, `Get(key, receiver)`, `Set(key, value, receiver)`, `Delete`, `OwnPropertyKeys`, and `Call` /
`Construct(arguments, newTarget)`. The `Ordinary*` methods are the ordinary versions, callable from an override.

An exotic object must set `ObjectFlags.Exotic` in its constructor: the fast paths of the ordinary objects (direct slot
reads) are disabled for it. `ObjectFlags.Callable`/`Constructor` give `IsCallable`/`IsConstructor`.

`JsObject.Class` (`ObjectClass`) identifies the internal slots of the built-in objects (e.g. `ObjectClass.Map` for
[[MapData]]): test it instead of the .NET type when the spec says "has a [[XxxData]] internal slot".

Storage: the properties are in a `PropertyTable` (insertion order); the array indices with the default attributes are
in a dense `JsValue[]` (holes are `JsValue.Hole`). `SetOwnSlot`/`RemoveOwnSlot` write the storage without any check
(use them only on objects being created, or after the spec checks). `DefineBuiltIn(key, value, flags)` defines a
built-in property (default: writable, configurable, not enumerable), `DefineBuiltInAccessor`, `DefineLazy` (computed on
first read).

Spec helpers on `JsObject`: `Get(key)`, `Set(key, value, throwOnFailure)`, `CreateDataProperty(OrThrow)`,
`DefinePropertyOrThrow`, `DeletePropertyOrThrow`, `HasOwnProperty`. In `ObjectOperations`: `GetFunctionRealm`,
`GetPrototypeFromConstructor`, `GetPrototypeForConstruct`, `OrdinaryHasInstance`, `InstanceOf`, `SpeciesConstructor`,
`CreateNonEnumerableDataPropertyOrThrow`. In `IteratorOperations`: `GetIterator`, `IteratorStepValue`, `IteratorClose`,
`IteratorCloseOnThrow`, `IterableToList`, `CreateIteratorResultObject`.

## Abstract operations

`JsOperations` (internal) has the conversions and comparisons of the spec: `ToPrimitive`, `ToBoolean`, `ToNumber`,
`ToNumeric`, `ToString`/`ToJsString`, `ToPropertyKey`, `ToObject`, `ToIntegerOrInfinity`, `ToInt32`, `ToUint32`,
`ToLength`, `ToIndex`, `SameValue`, `SameValueZero`, `IsStrictlyEqual`, `IsLooselyEqual`, `GetMethod`, `GetV`, `Call`,
`Invoke`, `Construct`, `IsCallable`, `IsConstructor`, `IsArray`, `TypeOf`, `LengthOfArrayLike`.
`NumberFormatting.ToString(double)` is Number::toString; `NumberParsing.StringToNumber` is StringToNumber.

## Errors

A JavaScript exception thrown by .NET code is a `JsException` carrying the thrown `JsValue`. Throw the native errors with
`ThrowHelper.ThrowTypeError("message")` (and `ThrowRangeError`, `ThrowReferenceError`, `ThrowSyntaxError`, generic
versions `ThrowTypeError<T>` for expressions): they create the error object in the current realm of the engine. A
`JsTerminationException` stops the script (limits, cancellation) and must never be caught by JavaScript code: never catch
`Exception` in the engine; catch `JsException` only.

## Engine, realms, intrinsics

`JsEngine` is an agent: `JsEngine.Current` is the engine running on the thread (set by `engine.Enter()` at every public
entry point), `engine.CurrentRealm` is the realm of the running execution context. A call into a built-in function
switches the current realm to the realm of the function (`JsBuiltinFunction.Call`), and checks the recursion limit.

`JsRealm` owns the `Intrinsics` and the global object. `Intrinsics` is a partial class: the prototype objects are
created first (constructor of `Intrinsics`), then `Initialize` calls each built-in.

### Writing a built-in

```csharp
internal static class MapBuiltins
{
    public static void Initialize(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;
        var prototype = intrinsics.MapPrototype;
        var constructor = realm.CreateBuiltinFunction("Map", 0, Construct, isConstructor: true);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);
        prototype.DefineBuiltIn("constructor", constructor);
        realm.DefineMethod(prototype, "get", 1, Get);
        realm.DefineAccessor(prototype, "size", GetSize, setter: null);
        prototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "Map", PropertyFlags.Configurable);
        intrinsics.AddGlobal("Map", constructor);
    }

    // newTarget is null for [[Call]]
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (newTarget is null)
            return ThrowHelper.ThrowTypeError<JsValue>("Constructor Map requires 'new'");

        var prototype = ObjectOperations.GetPrototypeForConstruct(function, newTarget, static i => i.MapPrototype);
        ...
    }
}
```

- The function names and lengths, the property attributes and the order of the observable operations follow the spec
  exactly (test262 checks them, e.g. `propertyHelper.js`).
- Read the arguments with `arguments.Length > i ? arguments[i] : default` (missing arguments are `undefined`).
- The objects created by a built-in are created in the realm of the function (`function.Realm`); the errors thrown by a
  built-in are created in the current realm (`ThrowHelper`), which is the realm of the function while it runs.
- Register the built-in in `Intrinsics` (a prototype property created in the constructor of `Intrinsics` when other
  built-ins reference it, and a call in `InitializeBuiltins`).

## Tests

- `tests/Meziantou.Framework.JavaScriptEngine.Tests`: unit tests (xUnit, the repo's `Assert`).
- `tests/Meziantou.Framework.JavaScriptEngine.Test262`: the conformance runner. Fetch the suites with
  `dotnet run eng/javascriptengine/fetch-tests.cs`. `test262-skip.txt` lists the areas not run yet;
  `expectations/<area>.txt` the known failures (`<id>:<mode>`, optional `# reason`). Update with
  `TEST262_UPDATE_EXPECTATIONS=1 dotnet test -f net10.0` (one target framework: the processes of the target frameworks
  run concurrently). Filter with `TEST262_FILTER=a|b` (substrings of the test ids). The update also regenerates
  `summary.md`.

## Conventions and pitfalls

- The build treats the analyzer warnings as errors. Public members need XML documentation.
- Write the non-ASCII characters of the source as `\uXXXX` escapes (never raw characters): the tools that write files
  sometimes turn an escape into the raw character, so check the files for characters above U+007F after writing them.
- No reflection, no dynamic code: the engine is trimming- and AOT-compatible.
- Never use the globalization of .NET (cultures, `CompareInfo`, `string.Normalize`, `TimeZoneInfo` names): the engine
  must behave the same in invariant-globalization mode. Unicode data comes from `Meziantou.Framework.Unicode`.
