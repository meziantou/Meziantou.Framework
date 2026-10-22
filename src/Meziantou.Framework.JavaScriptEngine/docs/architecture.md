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
| `Compiler/` | Scope analysis and the bytecode generator (the nested functions are compiled on their first call: see "Lazy compilation"). |
| `Interpreter/` | The bytecode interpreter: frames on the heap, no .NET recursion for JavaScript-to-JavaScript calls. |
| `Runtime/` | The object model and the abstract operations shared by everything. |
| `Builtins/` | The built-in objects of the realm: one static class per built-in (e.g. `ArrayBuiltins`). |
| `Temporal/` | The Temporal objects (`JsPlainDate`, `JsZonedDateTime`... with their internal slots), the abstract operations (`TemporalOperations`: exact `Int128` epoch nanoseconds and time durations, the rounding family), the RFC 9557 / ISO 8601 parser (`TemporalParser`) and the calendars (`ITemporalCalendar`, `TemporalCalendars`: ISO 8601, and in `Temporal/Calendars/` the calendars of ECMA-402 ported from ICU4X: `NonIsoTemporalCalendar` implements the NonISO* operations of the Intl era and monthCode proposal from the primitives of each calendar, `TemporalCalendarData` holds the year data of the Chinese, Korean and Umm al-Qura calendars). |
| `TimeZones/` | The embedded IANA time zone database used by Date and Temporal (`TimeZoneDatabase`, `NamedTimeZone`). |

## Lazy compilation

The parser parses the whole source text with its early errors, but the compiler only compiles the top level: the body of
a nested function is compiled on its first call, as V8 does (`Compiler/ScopeAnalyzer.Lazy.cs`, `Compiler/LazyFunction.cs`).

- The scope analysis of the enclosing code does not visit the body of a nested function: a scan collects the names of
  its identifiers (a superset of its free variables) and, for an arrow function, whether it uses the `this`,
  `new.target` or `super` of its enclosing function. These bindings are stored in the environments (captured), as if the
  function referenced them. The syntax tree of the body is then dropped.
- The template of the function (`FunctionTemplate`) has what creating the function object needs (the name, the length,
  the flags, the source range, the environment of the name of a named function expression) and a `LazyFunction`: the
  position and the kind of the function in the source text, and an immutable copy of the scopes that enclose it
  (`ScopeSnapshot`: the bindings, where they are stored, the environments).
- On the first call (`FunctionTemplate.EnsureCompiled`, from the creation of the frame), the function is parsed again
  (`JsParser.ParseLazyFunction`: the syntax tree has the positions of the first parse), analyzed in new scopes created
  from the snapshot, and compiled. The template stays immutable and shareable: the code is compiled once under a lock,
  and published to the other threads once it is complete. The nested functions of the function are lazy in turn.
- A function that contains a direct eval (in its body or in a nested function) is compiled with its enclosing code: the
  eval code can reference any binding. The function of a dynamic function (the Function constructor) is compiled
  immediately; its nested functions are lazy.
- A lazily compiled function that references a binding stored in a register of its enclosing code (the scan missed a
  name) is an internal error (`InvalidOperationException`), not a wrong result.

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

Storage: the named properties are in a `PropertyMap`: a `Shape` shared with the objects that got the same properties in
the same order (the keys and the attributes; the values are in the slots of the object), or a `PropertyTable` of the
object (dictionary mode: after a deletion other than the last property, a reconfiguration, a lazy property, an array
index outside the elements, more than 64 properties). The array indices with the default attributes are in a dense
`JsValue[]` (holes are `JsValue.Hole`). See "Performance" below. `SetOwnSlot`/`RemoveOwnSlot` write the storage without any check
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
created first (constructor of `Intrinsics`), then `Initialize` calls each built-in. `JsRealm.GlobalThis` is the this
value of the global code ([[GlobalThisValue]]): the global object, unless the host sets another object with
`SetGlobalThis` (the WindowProxy of a browser window); the code reads it at run time (`LoadGlobalThis`, the this of the
sloppy functions called without this value, the indirect evals).

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

## Jobs and promises

The job queue of the agent is in `JsEngine.Jobs.cs`. A job is a `JsJob`: a static `JsJobAction(state, argument)` (no
closure), its state and argument, and the realm it runs in. `engine.EnqueuePromiseJob` / `EnqueueGenericJob` (callable
from any thread) / `EnqueueTimeoutJob` go through the host hooks (`JsHost`, `engine.HostHooks`), whose default
implementations queue the jobs in the engine. `engine.RunJobs()` runs the promise jobs first, then the generic jobs and
the due timeout jobs (on the `TimeProvider` of the options), until the queue is empty; it clears the WeakRef kept
objects after each job. A job exception goes to `JsHost.ReportJobException` (default: rethrown by `RunJobs`).
`EnterNestedJobQueue()` redirects the new jobs to a separate queue until it is disposed. A host that has its own event
loop overrides `EnqueueGenericJob` / `EnqueueTimeoutJob`: it gets a `JsHostJob`, which it runs later with `Run()` (in
the realm of the job; the promise jobs it enqueues wait for the next `RunJobs`). The other hooks:
HostPromiseRejectionTracker, HostMakeJobCallback / HostCallJobCallback (a host state captured by `then` and restored when
the reaction runs). The .NET continuations (below) have no function object: `MakeContinuationHostDefined` captures their
host state, and `CallContinuation` restores it around `JsContinuation.Invoke()`; when it returns null (the default),
nothing is allocated and the continuation is called directly.

`JsPromise` (`Runtime/JsPromise.cs`) has the promise abstract operations: `PromiseCapability.Create(C)` /
`CreateIntrinsic(realm)` (NewPromiseCapability; the capabilities of %Promise% do not create the resolving functions
until they are needed), `capability.Resolve(value)` / `Reject(reason)`, `JsPromise.PromiseResolve(C, x)`,
`JsPromise.PerformPromiseThen(promise, onFulfilled, onRejected, capability)`. For `await`:
`JsPromise.PromiseResolve(realm, value)` and `JsPromise.PerformPromiseThen(promise, PromiseContinuation, PromiseContinuation, state)`
(.NET continuations: no function object is created).

## Explicit resource management

`Runtime/DisposableOperations.cs` has the operations of `using` / `await using`: `NewDisposeCapability()`,
`AddDisposableResource(capability, value, hint)` (hint `DisposeHint.SyncDispose` / `AsyncDispose`), `GetDisposeMethod`,
and `DisposeResources(capability, completion)`, where a completion is the thrown value of a throw completion or null
(any other completion): it returns the thrown value of the resulting completion (SuppressedError chaining), or null when
the original completion stands. For `await using`, the interpreter drives an `AsyncDisposeResourcesOperation`: while
`MoveNext(out value)` returns true, it performs `Await(value)` like the `await` operator, then calls
`SetAwaitResult(thrownOrNull)`; at the end `Completion` is the resulting completion. Built-in code uses
`DisposeResourcesAsync`, which drives it with promise continuations and returns a promise.

## Intl (ECMA-402)

`Intl/` has the services of ECMA-402, installed by `Runtime/Intrinsics.Intl.cs`. Every locale data comes from the
embedded CLDR data (`Intl/Data/`, `CldrData.Default`): never from the globalization of .NET. The default locale is
`JsEngineOptions.DefaultLocale` ("en-US").

- `Intl/Locale/LanguageTag.cs`: the parser of the Unicode BCP 47 locale identifiers (IsStructurallyValidLanguageTag)
  and `Canonicalize()` (CanonicalizeUnicodeLocaleId, with the alias rules of `LocaleCanonicalizer`).
  `LocaleCanonicalizer.CanonicalizeUnicodeValue(key, value)` is CanonicalizeUValue.
- `Intl/IntlOperations.cs`: CanonicalizeLocaleList, ResolveLocale (the lookup matcher for both matchers; a service gives
  the supported values of its relevant extension keys with a `LocaleKeyData` delegate, and optionally its own
  [[AvailableLocales]]), SupportedLocales, GetOptionsObject, CoerceOptionsToObject, GetOption (`GetStringOption`,
  `GetBooleanOption`), GetNumberOption, DefaultNumberOption.
- `Intl/Locale/LocaleInfo.cs`: the available calendars, collations, currencies, numbering systems and units
  (Intl.supportedValuesOf), and the regional preferences of a locale (calendars, hour cycles, week, time zones).
- `Intl/NumberFormat/IntlDecimal.cs`: the Intl mathematical values (exact decimal values, with negative zero, NaN and
  the infinities): from a Number, a BigInt, or a string (ToIntlMathematicalValue). Since Intl.NumberFormat v3, a Number
  is converted with Number::toString, so its shortest round-tripping decimal is rounded (`FromNumber`:
  `(1.005).toLocaleString("en", { maximumFractionDigits: 2 })` is "1.01", as in browsers); RelativeTimeFormat keeps
  the exact binary value of the spec text (`FromDouble`, ℝ(value)).
  `DigitOptions` is SetNumberFormatDigitOptions and FormatNumericToString (ToRawPrecision, ToRawFixed, the rounding
  modes, increments and priorities): Intl.PluralRules and Intl.NumberFormat share it, and so can the other services
  that format numbers.
- `Intl/NumberFormat/NumberFormatter.cs`: PartitionNumberPattern, from the CLDR number patterns (`NumberPattern`),
  the symbols of the numbering system and the compact, unit and currency patterns; the parts are grouped as the
  modifiers of ICU (outer, middle, inner) for the collapsing of the ranges (PartitionNumberRangePattern). Where ECMA-402
  leaves the output to the implementation, it follows ICU (Node.js is a convenient oracle to compare with).
- `Intl/Collator/`: Intl.Collator and String.prototype.localeCompare, a UCA collator over the CLDR root collation.
  `RootCollation` converts allkeys_CLDR.txt to 64-bit CEs (`CollationWeights`: 32-bit primaries, a root primary `p` is
  `p << 16` so that a tailoring can insert weights after it; the implicit pairs are single primaries) and derives the
  sorted root weights, the variable range and the script groups of `[reorder]`. `CollationBuilder` applies the rules of
  a tailoring as ICU's CollationBuilder (lists of nodes after the root primaries, then consecutive weights in the gaps):
  resets, `[before n]`, the special positions, prefixes, extensions, contractions, `[suppressContractions]`.
  `CollationTable` computes the CEs of a string in NFD (prefix mappings, contiguous and discontiguous contractions,
  implicit weights, numeric collation). `TailoredCollation` caches the table of each tailoring (the rules are the key)
  and `StringCollator` compares two strings with the settings (strength, case level, case first, shifted, numeric,
  backwards secondary, reordering): it skips the common prefix up to a safe boundary, compares the characters below
  U+0300 with precomputed CEs and an early exit on the first primary difference, and decomposes the other strings in
  thread-static buffers (no allocation). Node.js (ICU) is the oracle: the orders of every tailoring match it.
- `Intl/DateTimeFormat/`: Intl.DateTimeFormat with the amendments of Temporal (the formats of the Temporal types,
  `DateTimeFormatBuiltins`) and the toLocaleString methods of Date and of the Temporal types. A format is a UTS 35
  pattern (`DateTimeFormatRecord`); the components that resolvedOptions reports are read from the pattern. The patterns
  come from `DateTimePatternGenerator`, a port of ICU's DateTimePatternGenerator (the skeleton of the options matched
  against the CLDR availableFormats, the field widths adjusted, the date and the time combined with dateTimeFormats,
  the appendItems), so the formats are the ones of V8 and SpiderMonkey; "basic" uses it too. The hour fields use the
  [[HourCycle]] (V8's ReplaceHourCycleInPattern). `DateIntervalFormatter` is ICU's DateIntervalFormat (the CLDR
  intervalFormats and the fallbacks). `DateTimeLocalTimeCalculator` computes ToLocalTime with the calendars of Temporal
  (`TemporalCalendars`), `TimeZoneNameFormatter` the time zone names (metazones, generic and location formats, localized
  GMT, as ICU's TimeZoneFormat). The narrow no-break spaces of CLDR are spaces, as in the browsers.
- `Intl/Segmenter/`: Intl.Segmenter. The graphemes and the sentences are the ones of UAX #29 (Meziantou.Framework.Unicode);
  the words are the ones of the word break iterator of ICU, as V8 uses it (`WordBreaker`: the root rules of `word.txt`,
  then the dictionary break engines of `dictbe.cpp` for the segments that have Han, kana, Thai, Lao, Khmer or Burmese
  characters). The engines and the word lists of ICU are in Meziantou.Framework.Unicode.Dictionaries
  (`IcuDictionaryBreakEngine`, `IcuWordDictionary`, shared with Meziantou.Framework.WebPlatform): the Chinese and Japanese
  engine keeps the segmentation of a run of kana and kanji whose words have the smallest total cost, the other ones the
  longest words followed by other words of the list. A list is decompressed and loaded into a trie on the first use of
  its script (about 3 MB of memory for Chinese and Japanese). `WordBreaker` keeps what is specific to the break iterator
  of ICU: the runs given to the engines and the unhandled engine (`EngineStack`). `Corpus/SegmenterV8.jsonl` of the
  tests has words recorded with V8.

## Performance

- Interpreter (`JsInterpreter.Execute`): the dispatch loop only has the frequent instructions; the others are one small
  method each (`JsInterpreter.Instructions.cs`, reached through `ExecuteCold`: `frame.Pc` is the position of the
  instruction, they return the new stack pointer). Keep the loop small: a big method makes the JIT spill `pc`/`sp`,
  zero a large frame and stop inlining. The slow paths are calls that take values (no `ref` to the locals of the loop).
  `JsValue.Store`/`StoreNumber` skip the write barrier when a number overwrites a number.
- Frames: the ordinary calls rent their `Frame` from `engine.FramePool` (the arguments are copied after the registers in
  `Frame.Values`); generators, async functions, scripts, modules and eval code have frames of their own. Nothing may keep
  a reference to a pooled frame after its call.
- Bytecode: comparisons and logical nots followed by a conditional jump are fused (`JumpIfLessThan`...), loops test their
  condition at the end, updates whose value is not used are prefix updates (`IncrementLocal` on a register), method calls
  use `GetMethod`. A backward jump counts an interrupt point.
- Shapes (`Runtime/Shape.cs`): the empty shapes are static (`Shape.EmptyOrdinary`, `EmptyArray`, `EmptyIndexedExotic`);
  the shapes with properties belong to an engine (`engine.ShapeRoots`) and are not thread-safe. The exotic objects whose
  named properties are not ordinary (proxies, typed arrays, namespaces, host objects that override their internal
  methods) never get a shape.
- Inline caches (`Runtime/InlineCache.cs`, `Interpreter/JsInterpreter.InlineCaches.cs`): GetProperty, GetMethod,
  SetProperty, DefineField, LoadGlobal and LoadGlobalOrUndefined have the index of their cache as last operand
  (`FunctionTemplate.Caches`, shared by the engines that run the code: an entry is immutable, and the entries that apply
  to the static empty shapes check their engine). The entries that depend on other objects check the epoch of
  `InlineCache`: the objects are flagged `InlineCacheHolder` (the positions of their properties are remembered: a
  removal, a reconfiguration or a compaction increments the epoch) or `InlineCachePrototype` (the absence of a property
  is remembered: any change does). The global lexical declarations increment `GlobalEnvironment.LexicalVersion`. A site
  keeps up to 4 entries (`PropertyCacheEntry.Next`, the most recent first: the dispatch loop checks the first one, the
  miss path the others); with more receivers, or after 16 misses, it becomes megamorphic: Get and Set then use the cache
  of the engine (`MegamorphicCache`, found by the map, the prototype and the key), the other instructions are not cached.
- Elements: the dense storage doubles when it grows. The storages of the large object heap (85 KB and more) have the
  sizes of the shared array pools: they are rented, and the storage that a growth replaces goes back to the pool (the
  storage never leaves its object). An array whose length is known (`new Array(n)`, the result of `map`) grows faster
  up to its length once it has 1024 elements.
- Iteration: `IteratorStepValue` calls the native step of the built-in next methods (`JsBuiltinFunction.IteratorStep`:
  no result object), and `GetIterator` of an array iterates the array without an iterator object while
  `Intrinsics.IsArrayIterationIntact()` (the iterator object is created if something needs it). for-in enumerates the
  shapes (`ForInEnumerator`).
- Realms: the rarely used built-ins (Intl, Temporal, the binary data built-ins, RegExp, Date, the collections, WeakRef,
  FinalizationRegistry, Promise, the generators and async functions, the iterator helper prototypes, DisposableStack,
  AsyncDisposableStack) are created on first use: a lazy global (`AddLazyGlobal`, in the order of the eager globals) and
  intrinsics whose getters call `EnsureXxx`; the `AddGlobal` calls of their `Initialize` methods are ignored once the
  intrinsics are initialized (the globals are defined directly on the global object while they are). The built-in
  methods are in static tables (`BuiltinMethodTable`, a collection expression of `BuiltinMethod` in the built-in class,
  shared by the realms of the process): `JsRealm.DefineMethods` defines them in order, and their function objects are
  created on first access (one `LazyBuiltinMethodTable` per object and table: the slot of a lazy property has its cell
  and an index, `LazyCell.Evaluate(index)`). A table entry is static: it cannot capture a local, use `DefineMethod` for
  that. `DefineAccessor` creates the accessor functions on first access (`LazyBuiltinAccessorPair`); use
  `DefineEagerMethod` when a built-in needs the function object (an intrinsic, a function shared by two properties, an
  iterator step). The evaluation of a lazy property does not invalidate the inline caches (they evaluate a lazy
  property before remembering it).
- Property tables (`Runtime/PropertyTable.cs`): 32-byte entries, an open-addressing index above 8 properties. An object
  with lazy properties (prototypes, global objects) uses a table, not a chain of shapes; a host can size it once with
  `JsObject.ReserveProperties` (and read the size of an object with `JsObject.OwnPropertyCount`, as the WebIDL bindings
  of WebPlatform do). A `BuiltinMethodTable` learns the number of properties of the objects that get its methods
  (`PropertyTable.CapacityHint`): the next realms allocate their tables once, at their final size
  (`JsObject.ReservePropertiesForBuiltIns`, also called before the constants of Math and Number). The global object
  learns its size the same way.
- Host objects: `JsHostObject(prototype, target, hasOrdinaryInternalMethods: true)` creates a host object that uses the
  fast paths (it must not override the methods that read or write properties).
  `JsHostObject.HasCallInternalMethod` gives a host object a [[Call]] internal method (`CallCore`, which runs in the
  realm of the caller), and `IsHtmlDdaObject` an [[IsHTMLDDA]] internal slot: both are used by `document.all`.
- Benchmarks (`benchmarks/Meziantou.Framework.JavaScriptEngine.Benchmarks`, net10.0): `quick [workload|file.js]`,
  `profile <workload|file.js|Parse|CreateRealm> <seconds>` (allocations, GC, CPU time per run), `harness <testharness.js>`,
  `dump <workload|file.js>` (the bytecode), and BenchmarkDotNet (`EngineBenchmarks`, `ParseBenchmarks`). Run them with
  `DOTNET_TieredCompilation=0` for steady-state timings of short runs.

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
