using System.Diagnostics;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.WasmEngine.Fuzz;

/// <summary>
/// The targets of the WebAssembly engine:
/// <list type="bullet">
/// <item>The validation and the compilation of a module agree, and a compiled module exposes its imports, exports and custom sections.</item>
/// <item>The conversion of the text format returns a binary module or throws a <see cref="FormatException"/>.</item>
/// <item>The instantiation of a compiled module, with imports created from their types, and the calls of its exported
/// functions, only throw the exceptions of the engine (or the interruption of the store).</item>
/// <item>The functions compiled to .NET code behave as the interpreted ones: the same results, traps, memories and globals.</item>
/// </list>
/// </summary>
public static class WasmEngineFuzzTargets
{
    private const int MaxMemorySize = 16 * 1024 * 1024;

    // The magic number and the version, the section ids, the types, and the opcodes of the control instructions, of the
    // legacy exception handling instructions (try, catch, throw, rethrow, delegate, catch_all) and of try_table
    private static readonly string[] BinaryTokens = ["\0asm\u0001\0\0\0", "\u0001", "\u0002", "\u0003", "\u0004", "\u0005", "\u0006", "\u0007", "\u0008", "\u0009", "\u000A", "\u000B", "\u000C", "\u000D", "\u0002@", "\u0003@", "\u0004@", "\u0006@", "\u0006\u007F", "\u0007\u0000", "\u0008\u0000", "\u0009\u0000", "\u0018\u0000", "\u0018\u0001", "\u0019", "\u001F@", "\u000C\u0000", "\u000E", "`", "i", "\u007F", "~", "}", "|", "{", "p", "o", " \u0000", "A\u0000", "\u0010\u0000", "û", "ü", "ý", "þ"];

    // The keywords of the text format
    private static readonly string[] TextTokens = ["(module", "(func", "(param", "(result", "(local", "(memory", "(table", "(global", "(export", "(import", "(tag", "(type", "(struct", "(array", "(field", "(mut", "(ref", "(elem", "(data", "(start", "shared", "i32", "i64", "f32", "f64", "v128", "funcref", "externref", "anyref", "exnref", "null", "local.get", "local.set", "global.get", "i32.const", "i64.const", "i32.add", "i32.load", "i32.store", "memory.grow", "table.grow", "call", "call_indirect", "return_call", "block", "loop", "if", "else", "end", "br", "br_if", "br_table", "unreachable", "try_table", "catch", "catch_all", "throw", "throw_ref", "try", "delegate", "rethrow", "struct.new", "array.new", "ref.null", "ref.func", "ref.i31", "any.convert_extern", "memory.atomic.notify", "memory.atomic.wait32", ")", "$f", "0", "1"];

    private static readonly WasmRuntime Runtime = new(new WasmRuntimeOptions { MaxMemorySize = MaxMemorySize, MaxStackSize = 256 * 1024 });

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("wasm-validate-compile", ValidateCompileTarget) { Seeds = GetSeeds, Dictionary = BinaryTokens },
        new("wasm-wat2wasm", WatToWasmTarget) { Seeds = GetTextSeeds, Dictionary = TextTokens },
        new("wasm-instantiate", InstantiateTarget) { Seeds = GetSeeds, Dictionary = BinaryTokens },
        new("wasm-compiled-interpreted", CompiledInterpretedTarget) { Seeds = GetSeeds, Dictionary = BinaryTokens },
    ];

    private static IEnumerable<FuzzSeed> GetSeeds()
    {
        foreach (var (name, text) in Modules)
        {
            yield return new FuzzSeed(name, WasmRuntime.WatToWasm(text));
        }
    }

    private static IEnumerable<FuzzSeed> GetTextSeeds()
    {
        foreach (var (name, text) in Modules)
        {
            yield return new FuzzSeed(name, FuzzText.Encode(text));
        }
    }

    private static void ValidateCompileTarget(ReadOnlySpan<byte> data)
    {
        var bytes = data.ToArray();
        var valid = Runtime.Validate(bytes);
        WasmModule module;
        try
        {
            module = Runtime.Compile(bytes);
        }
        catch (WasmCompileException)
        {
            FuzzAssert.False(valid, "Validate returns true, but Compile throws");
            return;
        }

        FuzzAssert.True(valid, "Validate returns false, but Compile succeeds");
        CheckModule(module);
    }

    private static void CheckModule(WasmModule module)
    {
        foreach (var import in module.Imports)
        {
            FuzzAssert.True(import.Type is not null, "An import has no type");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var export in module.Exports)
        {
            FuzzAssert.True(names.Add(export.Name), $"The export {FuzzAssert.Format(export.Name)} is duplicated");
        }

        foreach (var section in module.GetCustomSections("name"))
        {
            FuzzAssert.True(section.Length <= module.Bytes.Length, "A custom section is larger than the module");
        }
    }

    private static void WatToWasmTarget(ReadOnlySpan<byte> data)
    {
        var text = FuzzText.Decode(data);
        byte[] bytes;
        try
        {
            bytes = WasmRuntime.WatToWasm(text);
        }
        catch (FormatException)
        {
            return;
        }

        FuzzAssert.True(bytes.AsSpan().StartsWith("\0asm"u8), "The binary does not start with the magic number");

        // A module of the text format may still be invalid (e.g. a type mismatch): the validation and the compilation agree
        var valid = Runtime.Validate(bytes);
        try
        {
            CheckModule(Runtime.Compile(bytes));
            FuzzAssert.True(valid, "Validate returns false, but Compile succeeds");
        }
        catch (WasmCompileException)
        {
            FuzzAssert.False(valid, "Validate returns true, but Compile throws");
        }
    }

    private static void InstantiateTarget(ReadOnlySpan<byte> data)
    {
        WasmModule module;
        try
        {
            module = Runtime.Compile(data.ToArray());
        }
        catch (WasmCompileException)
        {
            return;
        }

        // Each instantiation and each call can run for a short time: the infinite loops are interrupted
        var deadline = 0L;
        using var store = Runtime.CreateStore(new WasmStoreOptions
        {
            CheckInterrupt = () =>
            {
                if (Stopwatch.GetTimestamp() > Volatile.Read(ref deadline))
                    throw new OperationCanceledException("The execution is too long");
            },
        });

        var imports = new List<WasmExtern>();
        foreach (var import in module.Imports)
        {
            if (CreateImport(store, import.Type) is not { } value)
                return;

            imports.Add(value);
        }

        WasmInstance instance;
        try
        {
            Volatile.Write(ref deadline, Stopwatch.GetTimestamp() + Stopwatch.Frequency / 5);
            instance = store.Instantiate(module, imports);
        }
        catch (Exception ex) when (ex is WasmException or OperationCanceledException)
        {
            return;
        }

        FuzzAssert.Equal(module.Exports.Count, instance.Exports.Count, "The instance does not have the exports of the module");
        foreach (var (name, value) in instance.Exports)
        {
            switch (value)
            {
                case WasmMemory { Type.Shared: false } memory:
                    FuzzAssert.Equal(memory.Size * WasmMemoryType.PageSize, (ulong)memory.Data.LongLength, $"The size of the memory {FuzzAssert.Format(name)} does not match its bytes");
                    break;

                case WasmFunction function:
                    try
                    {
                        Volatile.Write(ref deadline, Stopwatch.GetTimestamp() + Stopwatch.Frequency / 5);
                        var arguments = Array.ConvertAll(function.Type.Parameters, WasmValue.Default);
                        var results = function.Call(arguments);
                        FuzzAssert.Equal(function.Type.Results.Length, results.Length, "The function does not return its results");
                    }
                    catch (Exception ex) when (ex is WasmException or OperationCanceledException)
                    {
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// Runs the exported functions of a module with the interpreter, with the functions compiled to .NET code, and with
    /// the calls continuing in .NET code from their first loop iteration (OSR): the results, the traps and the exported memories and globals agree, until a call is interrupted or exhausts the stack
    /// (the compiled code uses the stack of .NET, so the limits are reached at another depth).
    /// </summary>
    private static void CompiledInterpretedTarget(ReadOnlySpan<byte> data)
    {
        // A failure of the compiler to .NET code is a failure of the target, instead of interpreting the function
        JitOps.ThrowOnInvalidProgram = true;
        var bytes = data.ToArray();
        CompareWithInterpreter(bytes, CompiledRuntime);
        CompareWithInterpreter(bytes, OsrRuntime);
    }

    /// <summary>Runs a module with the interpreter and with another runtime, and checks that the outcomes agree.</summary>
    private static void CompareWithInterpreter(byte[] bytes, WasmRuntime otherRuntime)
    {
        WasmModule interpretedModule;
        WasmModule compiledModule;
        try
        {
            interpretedModule = InterpretedRuntime.Compile(bytes);
            compiledModule = otherRuntime.Compile(bytes);
        }
        catch (WasmCompileException)
        {
            return;
        }

        var deadline = 0L;
        var options = new WasmStoreOptions
        {
            CheckInterrupt = () =>
            {
                if (Stopwatch.GetTimestamp() > Volatile.Read(ref deadline))
                    throw new OperationCanceledException("The execution is too long");
            },
        };
        using var interpretedStore = InterpretedRuntime.CreateStore(options);
        using var compiledStore = otherRuntime.CreateStore(options);
        if (CreateImports(interpretedStore, interpretedModule) is not { } interpretedImports || CreateImports(compiledStore, compiledModule) is not { } compiledImports)
            return;

        Volatile.Write(ref deadline, Stopwatch.GetTimestamp() + Stopwatch.Frequency / 5);
        var interpreted = Run(() => interpretedStore.Instantiate(interpretedModule, interpretedImports));
        Volatile.Write(ref deadline, Stopwatch.GetTimestamp() + Stopwatch.Frequency / 5);
        var compiled = Run(() => compiledStore.Instantiate(compiledModule, compiledImports));
        if (interpreted.Stop || compiled.Stop)
            return;

        FuzzAssert.Equal(interpreted.Outcome, compiled.Outcome, "The instantiation differs");
        if (interpreted.Value is not WasmInstance interpretedInstance || compiled.Value is not WasmInstance compiledInstance)
            return;

        for (var i = 0; i < interpretedInstance.Exports.Count; i++)
        {
            var (name, value) = interpretedInstance.Exports[i];
            if (value is not WasmFunction function)
                continue;

            var arguments = Array.ConvertAll(function.Type.Parameters, WasmValue.Default);
            Volatile.Write(ref deadline, Stopwatch.GetTimestamp() + Stopwatch.Frequency / 5);
            interpreted = Run(() => function.Call(arguments));
            Volatile.Write(ref deadline, Stopwatch.GetTimestamp() + Stopwatch.Frequency / 5);
            compiled = Run(() => ((WasmFunction)compiledInstance.Exports[i].Value).Call(arguments));
            if (interpreted.Stop || compiled.Stop)
                return;

            FuzzAssert.Equal(interpreted.Outcome, compiled.Outcome, $"The call of {FuzzAssert.Format(name)} differs");
            FuzzAssert.Equal(DescribeExports(interpretedInstance), DescribeExports(compiledInstance), $"The exports differ after the call of {FuzzAssert.Format(name)}");
        }
    }

    private static readonly WasmRuntime InterpretedRuntime = new(new WasmRuntimeOptions { MaxMemorySize = MaxMemorySize, MaxStackSize = 256 * 1024, CompilationThreshold = -1 });

    private static readonly WasmRuntime CompiledRuntime = new(new WasmRuntimeOptions { MaxMemorySize = MaxMemorySize, MaxStackSize = 256 * 1024, CompilationThreshold = 0 });

    // The calls continue in .NET code from the first iteration of their loops (OSR)
    private static readonly WasmRuntime OsrRuntime = new(new WasmRuntimeOptions { MaxMemorySize = MaxMemorySize, MaxStackSize = 256 * 1024, CompilationThreshold = int.MaxValue, OsrThreshold = 1 });

    private static List<WasmExtern>? CreateImports(WasmStore store, WasmModule module)
    {
        var imports = new List<WasmExtern>();
        foreach (var import in module.Imports)
        {
            if (CreateImport(store, import.Type) is not { } value)
                return null;

            imports.Add(value);
        }

        return imports;
    }

    /// <summary>Runs an action: its value, a description of its results or of its exception, and whether the comparison must stop (an interruption or the exhaustion of the stack).</summary>
    private static (object? Value, string Outcome, bool Stop) Run(Func<object> action)
    {
        try
        {
            var value = action();
            return (value, value is WasmValue[] results ? string.Join(' ', results.Select(Describe)) : "ok", Stop: false);
        }
        catch (OperationCanceledException)
        {
            return (null, "interrupted", Stop: true);
        }
        catch (WasmTrapException ex)
        {
            return (null, "trap: " + ex.Message, ex.TrapKind == WasmTrapKind.StackOverflow);
        }
        catch (WasmException ex)
        {
            return (null, ex.GetType().Name + ": " + ex.Message, Stop: false);
        }
    }

    private static string Describe(WasmValue value) => value.Kind switch
    {
        WasmValueKind.Ref => value.Reference is null ? "null" : "ref",
        WasmValueKind.V128 => value.V128Low.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + value.V128High.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => value.I64.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>The contents of the exported memories that are not shared and the values of the exported globals.</summary>
    private static string DescribeExports(WasmInstance instance)
    {
        var description = new System.Text.StringBuilder();
        foreach (var (name, value) in instance.Exports)
        {
            switch (value)
            {
                case WasmMemory { Type.Shared: false } memory:
                    description.Append(name).Append('=').Append(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(memory.Data))).Append(';');
                    break;
                case WasmGlobal global:
                    description.Append(name).Append('=').Append(Describe(global.Get())).Append(';');
                    break;
            }
        }

        return description.ToString();
    }

    /// <summary>Creates a value of an import type, or returns null when the type has no default value (e.g. a non-nullable reference).</summary>
    private static WasmExtern? CreateImport(WasmStore store, WasmExternType type)
    {
        try
        {
            return type switch
            {
                WasmFunctionType function => store.CreateFunction(function, (_, results) =>
                {
                    for (var i = 0; i < results.Length; i++)
                    {
                        results[i] = WasmValue.Default(function.Results[i]);
                    }
                }),
                WasmMemoryType memory => store.CreateMemory(memory),
                WasmTableType table => store.CreateTable(table, WasmValue.Null),
                WasmGlobalType global => store.CreateGlobal(global, WasmValue.Default(global.ValueType)),
                WasmTagType tag => store.CreateTag(tag),
                _ => null,
            };
        }
        catch (WasmException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            // A concrete heap type cannot be created from its description
            return null;
        }
    }

    // The modules of the tests of the WebAssembly API (WebAssemblyTests.LegacyExceptionHandlingModule) and of the engine
    // (WasmRuntimeTests: the compilation to .NET code)
    private static readonly (string Name, string Text)[] Modules =
    [
        ("fib", """
            (module
              (func $fib (export "fib") (param i32) (result i32)
                (if (result i32) (i32.lt_u (local.get 0) (i32.const 2))
                  (then (local.get 0))
                  (else
                    (i32.add
                      (call $fib (i32.sub (local.get 0) (i32.const 1)))
                      (call $fib (i32.sub (local.get 0) (i32.const 2)))))))
              (func (export "fib20") (result i32) (call $fib (i32.const 20))))
            """),
        ("typed-calls", """
            (module
              (tag $e (param i64))
              (func $even (param i32) (result i32)
                (if (result i32) (i32.eqz (local.get 0)) (then (i32.const 1)) (else (call $odd (i32.sub (local.get 0) (i32.const 1))))))
              (func $odd (param i32) (result i32)
                (if (result i32) (i32.eqz (local.get 0)) (then (i32.const 0)) (else (call $even (i32.sub (local.get 0) (i32.const 1))))))
              (func $mix (param i32 i64 f32 f64 i32 i64 f32 f64) (result f64)
                (f64.add (f64.add (f64.convert_i32_s (local.get 0)) (f64.convert_i64_s (local.get 1)))
                  (f64.add (f64.promote_f32 (local.get 2)) (f64.mul (local.get 3) (f64.convert_i32_u (local.get 4))))))
              (func $many (param i32 i32 i32 i32 i32 i32 i32 i32 i32) (result i32)
                (i32.add (local.get 0) (local.get 8)))
              (func $throw_if (param i64) (result i64)
                (if (i64.eqz (local.get 0)) (then (throw $e (i64.const 42))))
                (i64.sub (local.get 0) (i64.const 1)))
              (func $catch (param i64) (result i64)
                (block $caught (result i64)
                  (try_table (catch $e $caught)
                    (return (call $throw_if (call $throw_if (local.get 0)))))
                  (unreachable)))
              (type $step (func (param i32) (result i32)))
              (table $table 3 funcref)
              (elem (table $table) (i32.const 0) func $even $odd)
              (func (export "indirect") (result i32)
                (local $i i32) (local $sum i32)
                (loop $next
                  (local.set $sum (i32.add (local.get $sum)
                    (call_indirect $table (type $step) (local.get $i) (i32.and (local.get $i) (i32.const 1)))))
                  (local.set $sum (i32.add (local.get $sum) (call_ref $step (local.get $i) (ref.func $odd))))
                  (br_if $next (i32.lt_u (local.tee $i (i32.add (local.get $i) (i32.const 1))) (i32.const 20))))
                (local.get $sum))
              (func (export "indirect_mismatch") (result f64)
                (call_indirect $table (param i32) (result f64) (i32.const 1) (i32.const 0)))
              (func (export "indirect_uninitialized") (result i32)
                (call_indirect $table (type $step) (i32.const 1) (i32.const 2)))
              (func (export "indirect_swap") (result i32)
                (local $sum i32)
                (local.set $sum (call_indirect $table (type $step) (i32.const 4) (i32.const 0)))
                (table.set $table (i32.const 0) (ref.func $odd))
                (local.set $sum (i32.add (local.get $sum) (call_indirect $table (type $step) (i32.const 4) (i32.const 0))))
                (table.set $table (i32.const 0) (ref.func $even))
                (local.get $sum))
              (func (export "indirect_negative") (result i32)
                (call_indirect $table (type $step) (i32.const 1) (i32.const -1)))
              (func (export "even") (result i32) (call $even (i32.const 101)))
              (func (export "mix") (result f64)
                (call $mix (i32.const -3) (i64.const 5) (f32.const 1.5) (f64.const 2.25) (i32.const 4) (i64.const 0) (f32.const 0) (f64.const 0)))
              (func (export "many") (result i32)
                (call $many (i32.const 1) (i32.const 2) (i32.const 3) (i32.const 4) (i32.const 5) (i32.const 6) (i32.const 7) (i32.const 8) (i32.const 9)))
              (func (export "catch") (result i64)
                (local $i i64) (local $sum i64)
                (loop $next
                  (local.set $sum (i64.add (local.get $sum) (call $catch (i64.and (local.get $i) (i64.const 3)))))
                  (br_if $next (i64.lt_u (local.tee $i (i64.add (local.get $i) (i64.const 1))) (i64.const 50))))
                (local.get $sum)))
            """),
        ("floats-memory-inlining", """
            (module
              (memory (export "memory") 1)
              (global $g (export "g") (mut f64) (f64.const 0))
              (func $square (param f64) (result f64) (f64.mul (local.get 0) (local.get 0)))
              (func $store (param i32 f32) (f32.store offset=4 (local.get 0) (local.get 1)))
              (func $nan (result f32) (f32.const nan:0x200000))
              (func (export "run") (param $n i32) (result f64)
                (local $i i32) (local $x f64) (local $y f32)
                (f64.const 0.5)
                (loop $next (param f64) (result f64)
                  (local.set $x (f64.add (call $square (local.get $x)) (f64.convert_i32_u (local.get $i))))
                  (call $store (i32.mul (local.get $i) (i32.const 8)) (f32.demote_f64 (local.get $x)))
                  (local.set $y (f32.load offset=4 (i32.mul (local.get $i) (i32.const 8))))
                  (f64.store (i32.const 1024) (f64.promote_f32 (local.get $y)))
                  (f64.add (f64.load (i32.const 1024)))
                  (br_if $next (i32.lt_u (local.tee $i (i32.add (local.get $i) (i32.const 1))) (local.get $n))))
                (global.set $g (local.get $x))
                (f64.add (f64.convert_i32_s (i32.reinterpret_f32 (call $nan)))))
              (func (export "compare") (result i32)
                (i32.add
                  (i32.add (f64.lt (f64.const nan) (f64.const 1)) (f64.le (f64.const 1) (f64.const 1)))
                  (i32.add (f32.ge (f32.const -0) (f32.const 0)) (f64.ne (f64.const nan) (f64.const nan)))))
              (func (export "out_of_bounds") (result f64) (f64.load (i32.const 65530)))
              (func (export "fifty") (result f64) (call 3 (i32.const 50))))
            """),
        ("loop-memory-exceptions", """
            (module
              (memory (export "memory") 1)
              (global $count (export "count") (mut i64) (i64.const 0))
              (tag $e (param i32))
              (func $thrower (param i32) (throw $e (local.get 0)))
              (func $run (param i32) (result i32)
                (local $sum i32)
                (block $done
                  (loop $next
                    (br_if $done (i32.eqz (local.get 0)))
                    (i32.store (i32.const 0) (i32.add (i32.load (i32.const 0)) (local.get 0)))
                    (global.set $count (i64.add (global.get $count) (i64.const 1)))
                    (local.set 0 (i32.sub (local.get 0) (i32.const 1)))
                    (br $next)))
                (block $caught (result i32)
                  (try_table (catch $e $caught)
                    (call $thrower (i32.load (i32.const 0))))
                  (i32.const -1)))
              (func (export "run") (result i32) (call $run (i32.const 100)))
              (func (export "float") (result f64)
                (f64.add (f64.sqrt (f64.const 2)) (f64.convert_i32_s (i32.trunc_sat_f32_s (f32.const 1e10)))))
              (func (export "select") (result i64)
                (select (i64.const 1) (i64.const 2) (i32.load8_u (i32.const 0)))))
            """),
        ("catch", """
            (module
              (tag $e (param i32))
              (tag $f (param i64 i64))
              (import "env" "fail" (func $fail))
              (func (result i64)
                try (result i64)
                  i64.const 2
                  i64.const 40
                  throw $f
                catch $e
                  drop
                  i64.const -1
                catch $f
                  i64.add
                catch_all
                  call $fail
                  i64.const -2
                end))
            """),
        ("rethrow", """
            (module
              (tag $e (param i32))
              (func
                try
                  i32.const 1
                  throw $e
                catch $e
                  drop
                  try
                    i32.const 2
                    throw $e
                  catch $e
                    drop
                    rethrow 1
                  end
                end))
            """),
        ("delegate", """
            (module
              (tag $e (param i32))
              (func (result i32)
                try (result i32)
                  try (result i32)
                    try (result i32)
                      i32.const 7
                      throw $e
                    delegate 1
                  catch $e
                    drop
                    try (result i32)
                      i32.const 2
                      throw $e
                    delegate 0
                  end
                catch $e
                end)
              (func (result i32)
                try (result i32)
                  i32.const 3
                  throw $e
                delegate 0)
              (func (param i32) (result i32) (local i32)
                local.get 0
                if (result i32)
                  loop
                    try
                      local.get 1
                      throw $e
                    delegate 0
                  end
                  i32.const 1
                else
                  try (result i32)
                    i32.const 5
                    throw $e
                  delegate 0
                end))
            """),
        ("branches", """
            (module
              (type $s (struct (field i32)))
              (tag $e (param i32))
              (func (param i32) (result i32)
                local.get 0
                try (param i32) (result i32)
                  i32.const 1
                  i32.add
                  local.get 0
                  i32.eqz
                  br_if 0
                  throw $e
                catch $e
                  i32.const 100
                  i32.add
                  br 0
                end)
              (func (param i32) (result i32)
                try (result i32)
                  block $b1
                    block $b0
                      local.get 0
                      br_table $b0 $b1 $b1
                    end
                    block $h (result i32)
                      try_table (catch $e $h)
                        i32.const 10
                        throw $e
                      end
                      unreachable
                    end
                    v128.const i32x4 1 2 3 4
                    i32x4.extract_lane 2
                    i32.add
                    throw $e
                  end
                  block $cast (result (ref $s))
                    i32.const 20
                    struct.new $s
                    br_on_cast $cast (ref $s) (ref $s)
                    unreachable
                  end
                  struct.get $s 0
                  throw $e
                catch $e
                end))
            """),

        // The modules of the tests of the engine (WasmRuntimeTests)
        ("imports", """
            (module
              (import "env" "f" (func $f (param i32) (result i32)))
              (import "env" "m" (memory 1 2))
              (import "env" "t" (table 1 funcref))
              (import "env" "g" (global (mut i32)))
              (import "env" "e" (tag (param i32)))
              (func (export "run") (param i32) (result i32) (call $f (local.get 0))))
            """),
        ("memory", """
            (module
              (memory (export "memory") 1 2)
              (func (export "grow") (result i32) (memory.grow (i32.const 1)))
              (func (export "store") (param i32 i32) (i32.store (local.get 0) (local.get 1))))
            """),
        ("loop", """(module (func (export "loop") (loop $l (br $l))))"""),
        ("recursion", """(module (func $f (export "f") (call $f)))"""),
        ("shared-memory", """
            (module
              (memory (export "memory") 1 1 shared)
              (func (export "notify") (result i32) (memory.atomic.notify (i32.const 0) (i32.const 1))))
            """),
        ("gc", """
            (module
              (type $a (array (mut i16)))
              (func (export "new") (result (ref $a)) (array.new $a (i32.const 7) (i32.const 3))))
            """),
        ("trap", """
            (module
              (func (export "trap") unreachable)
              (func (export "div") (param i32) (result i32) (i32.div_s (i32.const 1) (local.get 0))))
            """),
        ("globals-tables", """
            (module
              (global (export "counter") (mut i32) (i32.const 1))
              (table (export "table") 2 10 funcref)
              (tag (export "tag") (param i32))
              (func (export "throw") (param i32) (throw 0 (local.get 0))))
            """),
    ];
}
