using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.WebPlatform.Wasmtime.Fuzz;

/// <summary>
/// The translation of the legacy exception handling instructions decodes the modules of the pages: whatever the bytes, it
/// returns a module or null, and the module it returns has no legacy instruction left and can be decoded again.
/// </summary>
public static class WasmtimeFuzzTargets
{
    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("wasmtime-legacy-exception-handling", LegacyExceptionHandlingTarget)
        {
            Seeds = GetSeeds,

            // The magic number and the version, the section ids, and the opcodes of the control instructions, of the legacy
            // exception handling instructions (try, catch, throw, rethrow, delegate, catch_all) and of try_table
            Dictionary = ["\0asm\u0001\0\0\0", "\u0001", "\u0003", "\u000A", "\u000D", "\u0002@", "\u0003@", "\u0004@", "\u0006@", "\u0006\u007F", "\u0007\u0000", "\u0008\u0000", "\u0009\u0000", "\u0018\u0000", "\u0018\u0001", "\u0019", "\u001F@", "\u000B", "\u0005", "\u000C\u0000", "\u000E", "`", "i"],
        },
    ];

    private static IEnumerable<FuzzSeed> GetSeeds()
    {
        foreach (var (name, text) in Modules)
        {
            yield return new FuzzSeed(name, WasmtimeRuntime.WatToWasm(text));
        }
    }

    private static void LegacyExceptionHandlingTarget(ReadOnlySpan<byte> data)
    {
        var translated = LegacyExceptionHandling.Translate(data.ToArray());
        if (translated is null)
            return;

        var again = LegacyExceptionHandling.Translate(translated, out var decoded);
        FuzzAssert.True(decoded, "The translated module cannot be decoded");
        FuzzAssert.True(again is null, "The translated module has legacy exception handling instructions");
    }

    // The modules of the tests of the WebAssembly API (WebAssemblyTests.LegacyExceptionHandlingModule)
    private static readonly (string Name, string Text)[] Modules =
    [
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
    ];
}
