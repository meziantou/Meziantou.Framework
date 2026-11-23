namespace Meziantou.Framework.WasmEngine.Benchmarks;

/// <summary>The module of the workloads: each export <c>run_*</c> takes a size and returns a checksum.</summary>
internal static class Workloads
{
    /// <summary>The workloads and their sizes.</summary>
    public static IReadOnlyDictionary<string, int> All { get; } = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        // Recursive calls: fib(25) makes 242785 calls
        ["fib"] = 25,

        // Integer arithmetic in a loop (locals only)
        ["loop"] = 1_000_000,

        // Loads and stores of the linear memory: the sieve of Eratosthenes
        ["sieve"] = 1_000_000,

        // Floating-point arithmetic: the points of the Mandelbrot set of a grid
        ["mandelbrot"] = 200,

        // Calls of a host function from WebAssembly
        ["hostcall"] = 100_000,

        // Calls of a function of the same module
        ["calls"] = 1_000_000,

        // Calls of a function of another module (an import)
        ["import"] = 1_000_000,

        // Indirect calls (call_indirect) of the functions of a table
        ["indirect"] = 1_000_000,
    };

    public const string Text = """
        (module
          (import "env" "inc" (func $inc (param i64) (result i64)))
          (import "lib" "inc" (func $lib_inc (param i64) (result i64)))
          (type $step (func (param i64) (result i64)))
          (table 2 funcref)
          (elem (i32.const 0) $add1 $add2)
          (func $add1 (type $step) (i64.add (local.get 0) (i64.const 1)))
          (func $add2 (type $step) (i64.add (local.get 0) (i64.const 2)))
          (memory (export "memory") 16)

          (func $fib (param $n i32) (result i32)
            (if (result i32) (i32.lt_u (local.get $n) (i32.const 2))
              (then (local.get $n))
              (else
                (i32.add
                  (call $fib (i32.sub (local.get $n) (i32.const 1)))
                  (call $fib (i32.sub (local.get $n) (i32.const 2)))))))

          (func (export "run_fib") (param $n i32) (result i64)
            (i64.extend_i32_u (call $fib (local.get $n))))

          (func (export "run_loop") (param $n i32) (result i64)
            (local $i i32) (local $acc i64)
            (loop $l
              (local.set $acc (i64.add (i64.mul (local.get $acc) (i64.const 6364136223846793005)) (i64.extend_i32_u (local.get $i))))
              (local.set $acc (i64.xor (local.get $acc) (i64.shr_u (local.get $acc) (i64.const 33))))
              (br_if $l (i32.ne (local.tee $i (i32.add (local.get $i) (i32.const 1))) (local.get $n))))
            (local.get $acc))

          (func (export "run_sieve") (param $n i32) (result i64)
            (local $i i32) (local $j i32) (local $count i64)
            (memory.fill (i32.const 0) (i32.const 0) (local.get $n))
            (local.set $i (i32.const 2))
            (block $done
              (loop $outer
                (br_if $done (i32.ge_u (local.get $i) (local.get $n)))
                (if (i32.eqz (i32.load8_u (local.get $i)))
                  (then
                    (local.set $count (i64.add (local.get $count) (i64.const 1)))
                    (local.set $j (i32.add (local.get $i) (local.get $i)))
                    (block $inner_done
                      (loop $inner
                        (br_if $inner_done (i32.ge_u (local.get $j) (local.get $n)))
                        (i32.store8 (local.get $j) (i32.const 1))
                        (local.set $j (i32.add (local.get $j) (local.get $i)))
                        (br $inner)))))
                (local.set $i (i32.add (local.get $i) (i32.const 1)))
                (br $outer)))
            (local.get $count))

          (func (export "run_mandelbrot") (param $n i32) (result i64)
            (local $x i32) (local $y i32) (local $k i32) (local $count i64)
            (local $cr f64) (local $ci f64) (local $zr f64) (local $zi f64) (local $t f64)
            (loop $ly
              (local.set $ci (f64.sub (f64.div (f64.mul (f64.convert_i32_u (local.get $y)) (f64.const 2)) (f64.convert_i32_u (local.get $n))) (f64.const 1)))
              (local.set $x (i32.const 0))
              (loop $lx
                (local.set $cr (f64.sub (f64.div (f64.mul (f64.convert_i32_u (local.get $x)) (f64.const 3)) (f64.convert_i32_u (local.get $n))) (f64.const 2)))
                (local.set $zr (f64.const 0))
                (local.set $zi (f64.const 0))
                (local.set $k (i32.const 0))
                (block $escaped
                  (loop $lk
                    (br_if $escaped (f64.gt (f64.add (f64.mul (local.get $zr) (local.get $zr)) (f64.mul (local.get $zi) (local.get $zi))) (f64.const 4)))
                    (local.set $t (f64.add (f64.sub (f64.mul (local.get $zr) (local.get $zr)) (f64.mul (local.get $zi) (local.get $zi))) (local.get $cr)))
                    (local.set $zi (f64.add (f64.mul (f64.mul (f64.const 2) (local.get $zr)) (local.get $zi)) (local.get $ci)))
                    (local.set $zr (local.get $t))
                    (br_if $lk (i32.lt_u (local.tee $k (i32.add (local.get $k) (i32.const 1))) (i32.const 100)))))
                (if (i32.eq (local.get $k) (i32.const 100))
                  (then (local.set $count (i64.add (local.get $count) (i64.const 1)))))
                (br_if $lx (i32.lt_u (local.tee $x (i32.add (local.get $x) (i32.const 1))) (local.get $n))))
              (br_if $ly (i32.lt_u (local.tee $y (i32.add (local.get $y) (i32.const 1))) (local.get $n))))
            (local.get $count))

          (func (export "run_hostcall") (param $n i32) (result i64)
            (local $acc i64)
            (loop $l
              (local.set $acc (call $inc (local.get $acc)))
              (br_if $l (local.tee $n (i32.sub (local.get $n) (i32.const 1)))))
            (local.get $acc))

          (func (export "run_calls") (param $n i32) (result i64)
            (local $acc i64)
            (loop $l
              (local.set $acc (call $add1 (local.get $acc)))
              (br_if $l (local.tee $n (i32.sub (local.get $n) (i32.const 1)))))
            (local.get $acc))

          (func (export "run_import") (param $n i32) (result i64)
            (local $acc i64)
            (loop $l
              (local.set $acc (call $lib_inc (local.get $acc)))
              (br_if $l (local.tee $n (i32.sub (local.get $n) (i32.const 1)))))
            (local.get $acc))

          (func (export "run_indirect") (param $n i32) (result i64)
            (local $acc i64)
            (loop $l
              (local.set $acc (call_indirect (type $step) (local.get $acc) (i32.and (local.get $n) (i32.const 1))))
              (br_if $l (local.tee $n (i32.sub (local.get $n) (i32.const 1)))))
            (local.get $acc))

          (func (export "identity") (param $n i32) (result i64)
            (i64.extend_i32_u (local.get $n))))
        """;

    private static string CreateLargeText(int functionCount)
    {
        var text = new System.Text.StringBuilder();
        text.Append("(module (memory 1)");
        for (var i = 0; i < functionCount; i++)
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $$"""
                (func $f{{i}} (param $n i32) (result i32)
                  (local $i i32) (local $acc i32)
                  (block $done
                    (loop $l
                      (br_if $done (i32.ge_u (local.get $i) (local.get $n)))
                      (if (i32.and (local.get $i) (i32.const 1))
                        (then (local.set $acc (i32.add (local.get $acc) (i32.load (i32.and (local.get $i) (i32.const 1020))))))
                        (else (i32.store (i32.and (local.get $acc) (i32.const 1020)) (local.get $i))))
                      (local.set $i (i32.add (local.get $i) (i32.const 1)))
                      (br $l)))
                  (call $f{{Math.Max(i - 1, 0)}} (i32.const 0))
                  (i32.add (local.get $acc)))
                """);
        }

        text.Append(')');
        return text.ToString();
    }

    /// <summary>The module whose function the module of the workloads imports (<c>lib.inc</c>).</summary>
    public const string LibraryText = """
        (module
          (func (export "inc") (param i64) (result i64)
            (i64.add (local.get 0) (i64.const 1))))
        """;

    public static byte[] Binary { get; } = WasmRuntime.WatToWasm(Text);

    /// <summary>A module of many functions with loops, branches, calls and memory accesses: the cost of the compilation of a large module.</summary>
    public static byte[] LargeBinary { get; } = WasmRuntime.WatToWasm(CreateLargeText(functionCount: 2000));

    public static byte[] LibraryBinary { get; } = WasmRuntime.WatToWasm(LibraryText);
}
