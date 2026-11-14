using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.JavaScriptEngine.Fuzz;

/// <summary>
/// <c>Atomics</c> on the <c>SharedArrayBuffer</c> of a block whose waiters are the waiters of another runtime
/// (<see cref="JsSharedDataBlock.Waiters"/>): whatever the views, the indices, the values and the timeouts of a script, the
/// engine calls them only with a location within the block, aligned on the size of its value (4 bytes for a notification,
/// 4 or 8 for a wait), a non-negative count, the current value of the location as the expected value, and a positive
/// timeout; the script throws a <see cref="JsException"/> or is stopped by a limit, nothing else.
/// </summary>
public static partial class JavaScriptEngineFuzzTargets
{
    private static readonly string[] AtomicsWaitersTokens =
    [
        "sab", "new Int32Array(sab)", "new BigInt64Array(sab)", "new Int16Array(sab)", "new Uint8Array(sab)", "new DataView(sab)", "new Int32Array(sab, 4)", "new BigInt64Array(sab, 8, 1)",
        "Atomics.wait(", "Atomics.waitAsync(", "Atomics.notify(", "Atomics.store(", "Atomics.load(", "Atomics.add(", "Atomics.compareExchange(", "sab.grow(", ".value.then(", ".async",
        "0", "1", "-1", "3", "7", "15", "0n", "-1n", "2n ** 63n", "NaN", "Infinity", "-Infinity", "0.5", "1e9", "undefined", "'1'", "{ valueOf() { sab.grow(64); return 0; } }",
    ];

    private const string AtomicsWaitersSeed = """
        const i32 = new Int32Array(sab);
        i32[1] = 5;
        var results = [Atomics.notify(i32, 1), Atomics.notify(i32, 2, 3), Atomics.wait(i32, 1, 5, 20), Atomics.wait(i32, 1, 6), Atomics.wait(i32, 1, 5, 0), Atomics.wait(new BigInt64Array(sab), 1, 0n)];
        const { async, value } = Atomics.waitAsync(i32, 3, 0, 100);
        value.then(result => results.push(async, result));
        """;

    private static IEnumerable<FuzzSeed> GetAtomicsWaitersSeeds()
    {
        yield return FuzzSeed.FromText("atomics-waiters", AtomicsWaitersSeed);
        yield return FuzzSeed.FromText("atomics-waiters-grow", "sab.grow(32); const v = new Int32Array(sab); Atomics.store(v, 7, -2); [Atomics.notify(v, 7, Infinity), Atomics.wait(v, 7, -2), Atomics.waitAsync(v, 7, -2, Infinity).async]");
        yield return FuzzSeed.FromText("atomics-waiters-bigint", "const b = new BigInt64Array(sab); Atomics.store(b, 1, -1n); [Atomics.wait(b, 1, -1n, NaN), Atomics.waitAsync(b, 0, 0n, 0).value, Atomics.waitAsync(b, 1, 1n).value]");
        yield return FuzzSeed.FromText("atomics-waiters-errors", "const errors = []; for (const f of [() => Atomics.wait(new Int16Array(sab), 0, 0), () => Atomics.notify(new Int32Array(sab), 4), () => Atomics.wait(new Int32Array(new ArrayBuffer(8)), 0, 0), () => Atomics.waitAsync(new Int32Array(sab), -1, 0)]) { try { f(); } catch (e) { errors.push(e.name); } } errors");
        yield return FuzzSeed.FromText("atomics-waiters-offset", "const v = new Int32Array(sab, 4, 2); Atomics.store(v, 1, 9); [Atomics.notify(v, 1, 0), Atomics.wait(v, 1, 9, 1), Atomics.notify(new Int32Array(sab), 3, -5)]");
    }

    // The input is a script that uses the views of 'sab', a growable SharedArrayBuffer whose waiters check their calls
    private static void AtomicsWaiters(ReadOnlySpan<byte> data)
    {
        var code = FuzzText.Decode(data);
        var block = JsSharedDataBlock.TryCreate(16, maxByteLength: 64)!;
        block.Waiters = new CheckingWaiters(block);
        using var engine = CreateEngine(loader: null);
        var realm = engine.CreateRealm();
        using (engine.Enter())
        {
            realm.GlobalObject.SetProperty("sab", realm.CreateSharedArrayBuffer(block));
        }

        Run(() => realm.Execute(code, "atomics.js"));
        Run(engine.RunJobs);
    }

    /// <summary>The waiters of another runtime: they check the calls of the engine, and complete every wait at once.</summary>
    private sealed class CheckingWaiters(JsSharedDataBlock block) : JsSharedDataBlockWaiters
    {
        private int _waits;

        public override int Notify(int byteIndex, int count)
        {
            CheckLocation(byteIndex, size: 4);
            FuzzAssert.True(count >= 0, $"Atomics.notify gives the negative count {count}");
            return Math.Min(count, 1);
        }

        public override bool Wait(int byteIndex, long expectedValue, int size, TimeSpan? timeout, CancellationToken cancellationToken)
        {
            CheckWait(byteIndex, expectedValue, size, timeout);
            return (_waits++ & 1) == 0;
        }

        public override bool WaitAsync(int byteIndex, long expectedValue, int size, TimeSpan? timeout, Action<bool> completed, CancellationToken cancellationToken)
        {
            CheckWait(byteIndex, expectedValue, size, timeout);

            // Every third wait is refused (the runtime has too many waiters): Atomics.waitAsync throws a RangeError
            var wait = _waits++;
            if (wait % 3 == 2)
                return false;

            completed((wait & 1) == 0);
            return true;
        }

        private void CheckWait(int byteIndex, long expectedValue, int size, TimeSpan? timeout)
        {
            FuzzAssert.True(size is 4 or 8, $"A wait has the size {size}");
            CheckLocation(byteIndex, size);
            FuzzAssert.True(timeout is null || timeout > TimeSpan.Zero, $"A wait has the timeout {timeout}");

            // The engine checks the value of the location before it waits: no other agent changes it here
            var location = block.GetSpan().Slice(byteIndex, size);
            var currentValue = size == 8 ? BitConverter.ToInt64(location) : BitConverter.ToInt32(location);
            FuzzAssert.Equal(currentValue, expectedValue, $"A wait at {byteIndex} expects another value than the value of the location");
        }

        private void CheckLocation(int byteIndex, int size)
        {
            FuzzAssert.True(byteIndex >= 0 && byteIndex % size == 0 && byteIndex <= block.ByteLength - size, $"The location {byteIndex} ({size} bytes) is not an aligned location of the block ({block.ByteLength} bytes)");
        }
    }
}
