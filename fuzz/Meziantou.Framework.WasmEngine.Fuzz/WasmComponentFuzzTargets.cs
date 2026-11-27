using System.Diagnostics;
using System.Text;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WasmEngine.Components;
using Meziantou.Framework.WasmEngine.Components.Runtime;

namespace Meziantou.Framework.WasmEngine.Fuzz;

/// <summary>
/// The targets of the component model:
/// <list type="bullet">
/// <item>The decoding of a component only throws a <see cref="WasmCompileException"/>; the instantiation of a component
/// without imports, and the calls of its exported functions with default values, only throw the exceptions of the engine.</item>
/// <item>The conversion of the text format of a component returns a component or throws a <see cref="FormatException"/>.</item>
/// <item>The canonical ABI: a value lifted from a memory is the same after it is lowered to another memory and lifted
/// again, and after it is lowered to core values and lifted again.</item>
/// </list>
/// </summary>
internal static class WasmComponentFuzzTargets
{
    private static readonly WasmRuntime Runtime = new(new WasmRuntimeOptions { MaxMemorySize = 16 * 1024 * 1024, MaxStackSize = 256 * 1024 });

    // The preamble, the section ids, and the opcodes of the types and of the canonical definitions
    public static readonly string[] BinaryTokens = ["\0asm\r\0\u0001\0", "\u0001", "\u0002", "\u0003", "\u0004", "\u0005", "\u0006", "\u0007", "\u0008", "\u0009", "\u000A", "\u000B", "\u0000\u0000", "\u0001\u0000", "@", "A", "B", "q", "r", "s", "t", "u", "v", "w", "x", "y", "z", "{", "|", "}", "~", "\u007F", "h", "i", "f", "e", "?", "<", "[constructor]", "[method]", "[static]", ".", "-", ":", "/", "@0.2.0", "@1", "wasi:io/streams"];

    public static readonly string[] TextTokens = ["(component", "(core module", "(core instance", "(core func", "(instance", "(instantiate", "(with", "(func", "(param", "(result", "(export", "(import", "(type", "(alias", "export", "outer", "(canon", "lift", "lower", "resource.new", "resource.drop", "resource.rep", "(memory", "(realloc", "(post-return", "async", "(callback", "string-encoding=utf8", "string-encoding=utf16", "string-encoding=latin1+utf16", "(record", "(field", "(variant", "(case", "(list", "(tuple", "(flags", "(enum", "(option", "(result", "(error", "(own", "(borrow", "(resource", "(rep i32)", "(dtor", "(sub resource)", "(stream", "(future", "stream.new", "stream.read", "stream.write", "future.new", "task.return", "waitable-set.new", "waitable-set.wait", "thread.yield", "u8", "u32", "s64", "f64", "char", "string", "bool", "(definition", "binary", "quote", ")", "$c", "0", "(implements", "(version-suffix", "(external-id", "(eq", "\"[method]r.f\"", "\"[constructor]r\"", "\"a:b/c@1.0.0\""];

    public static IEnumerable<FuzzTarget> All =>
    [
        new("component-compile", CompileTarget) { Seeds = GetSeeds, Dictionary = BinaryTokens },
        new("component-wat2wasm", WatToWasmTarget) { Seeds = GetTextSeeds, Dictionary = TextTokens },
        new("component-canonical-abi", CanonicalAbiTarget) { Seeds = GetCanonicalAbiSeeds },
    ];

    private static IEnumerable<FuzzSeed> GetSeeds()
    {
        foreach (var (name, text) in Components)
        {
            yield return new FuzzSeed(name, WasmRuntime.WatToWasm(text));
        }
    }

    private static IEnumerable<FuzzSeed> GetTextSeeds()
    {
        foreach (var (name, text) in Components)
        {
            yield return FuzzSeed.FromText(name, text);
        }
    }

    private static void CompileTarget(ReadOnlySpan<byte> data)
    {
        var bytes = data.ToArray();
        WasmComponent component;
        try
        {
            component = Runtime.CompileComponent(bytes);
        }
        catch (WasmCompileException)
        {
            return;
        }

        FuzzAssert.True(WasmRuntime.IsComponent(bytes), "A component that compiles does not start with the preamble of a component");
        if (component.Imports.Count > 0)
            return;

        var deadline = Stopwatch.GetTimestamp() + (Stopwatch.Frequency / 5);
        using var store = Runtime.CreateStore(new WasmStoreOptions
        {
            CheckInterrupt = () =>
            {
                if (Stopwatch.GetTimestamp() > Volatile.Read(ref deadline))
                    throw new OperationCanceledException("The execution is too long");
            },
        });

        WasmComponentInstance instance;
        try
        {
            instance = store.InstantiateComponent(component);
        }
        catch (WasmLinkException ex)
        {
            // The validation checks the instantiations: a valid component without imports links
            FuzzAssert.True(false, "A valid component without imports does not link: " + ex.Message);
            return;
        }
        catch (Exception ex) when (ex is WasmException or OperationCanceledException)
        {
            return;
        }

        foreach (var export in instance.Exports)
        {
            if (instance.GetFunction(export.Name) is not { Type: { } type } function)
                continue;

            var arguments = new ComponentValue[type.Parameters.Length];
            var callable = true;
            for (var i = 0; i < arguments.Length; i++)
            {
                if (GetDefault(type.Parameters[i].Type, depth: 0) is not { } value)
                {
                    callable = false;
                    break;
                }

                arguments[i] = value;
            }

            if (!callable)
                continue;

            Volatile.Write(ref deadline, Stopwatch.GetTimestamp() + (Stopwatch.Frequency / 5));
            try
            {
                function.Call(arguments);
            }
            catch (Exception ex) when (ex is WasmException or OperationCanceledException)
            {
                // A trap locks the store down: the other functions cannot be called
                return;
            }
        }
    }

    /// <summary>The default value of a type (zero, empty, the first case), or null for the handles and the streams.</summary>
    private static ComponentValue? GetDefault(ComponentValueType type, int depth)
    {
        if (depth > 16)
            return null;

        switch (type)
        {
            case ComponentPrimitiveType primitive:
                return primitive.Kind switch
                {
                    ComponentValueTypeKind.Bool => ComponentValue.FromBool(false),
                    ComponentValueTypeKind.S8 => ComponentValue.FromS8(0),
                    ComponentValueTypeKind.U8 => ComponentValue.FromU8(0),
                    ComponentValueTypeKind.S16 => ComponentValue.FromS16(0),
                    ComponentValueTypeKind.U16 => ComponentValue.FromU16(0),
                    ComponentValueTypeKind.S32 => ComponentValue.FromS32(0),
                    ComponentValueTypeKind.U32 => ComponentValue.FromU32(0),
                    ComponentValueTypeKind.S64 => ComponentValue.FromS64(0),
                    ComponentValueTypeKind.U64 => ComponentValue.FromU64(0),
                    ComponentValueTypeKind.F32 => ComponentValue.FromF32(0),
                    ComponentValueTypeKind.F64 => ComponentValue.FromF64(0),
                    ComponentValueTypeKind.Char => ComponentValue.FromChar(new Rune('a')),
                    ComponentValueTypeKind.String => ComponentValue.FromString("a"),
                    _ => null,
                };

            case ComponentListType { FixedLength: { } length } list:
                if (GetDefault(list.ElementType, depth + 1) is not { } element)
                    return null;

                return ComponentValue.FromList(Enumerable.Repeat(element, Math.Min(length, 1024)));

            case ComponentListType or ComponentMapType:
                return ComponentValue.FromList([]);

            case ComponentRecordType record:
                return GetDefaults(record.Fields.Select(field => field.Type), depth);

            case ComponentTupleType tuple:
                return GetDefaults(tuple.Types, depth);

            case ComponentEnumType enumeration:
                return ComponentValue.FromEnum(enumeration.Labels[0]);

            case ComponentFlagsType:
                return ComponentValue.FromFlags([]);

            case ComponentOptionType:
                return ComponentValue.NoneValue;

            case ComponentResultType result:
                if (result.Ok is null)
                    return ComponentValue.Ok();

                return GetDefault(result.Ok, depth + 1) is { } ok ? ComponentValue.Ok(ok) : null;

            case ComponentVariantType variant:
                var first = variant.Cases[0];
                if (first.Type is null)
                    return ComponentValue.FromCase(first.Name);

                return GetDefault(first.Type, depth + 1) is { } payload ? ComponentValue.FromCase(first.Name, payload) : null;

            default:
                return null;
        }
    }

    private static ComponentValue? GetDefaults(IEnumerable<ComponentValueType> types, int depth)
    {
        var values = new List<ComponentValue>();
        foreach (var type in types)
        {
            if (GetDefault(type, depth + 1) is not { } value)
                return null;

            values.Add(value);
        }

        return ComponentValue.FromRecord([.. values]);
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

        if (!WasmRuntime.IsComponent(bytes))
            return;

        try
        {
            Runtime.CompileComponent(bytes);
        }
        catch (WasmCompileException)
        {
            // The text is well-formed, but the component is not valid
        }
    }

    // ------------------------------------------------------------------------------------------------------------
    // Canonical ABI
    // ------------------------------------------------------------------------------------------------------------

    private static IEnumerable<FuzzSeed> GetCanonicalAbiSeeds()
    {
        // The type (see ReadType), the string encoding, then the content of the memory
        yield return new FuzzSeed("u32", new byte[] { 6, 0, 42, 0, 0, 0 });
        yield return new FuzzSeed("string", new byte[] { 12, 0, 8, 0, 0, 0, 5, 0, 0, 0, (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o' });
        yield return new FuzzSeed("string-utf16", new byte[] { 12, 1, 8, 0, 0, 0, 2, 0, 0, 0, (byte)'h', 0, (byte)'i', 0 });
        yield return new FuzzSeed("string-latin1", new byte[] { 12, 2, 8, 0, 0, 0, 2, 0, 0, 0, (byte)'h', (byte)'i' });
        yield return new FuzzSeed("list-u8", new byte[] { 13, 2, 0, 8, 0, 0, 0, 3, 0, 0, 0, 1, 2, 3 });
        yield return new FuzzSeed("record", new byte[] { 14, 2, 6, 12, 0, 7, 0, 0, 0, 16, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (byte)'o', (byte)'k' });
        yield return new FuzzSeed("variant", new byte[] { 16, 2, 255, 10, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xF8, 0x3F });
        yield return new FuzzSeed("option-result", new byte[] { 19, 20, 11, 12, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0x41, 0, 0, 0 });
        yield return new FuzzSeed("flags-enum", new byte[] { 15, 2, 18, 5, 17, 3, 0, 5, 0, 0, 0, 1 });
    }

    /// <summary>Reads a type: a kind, then its operands (the element types, the number of fields or cases).</summary>
    private static ComponentValueType ReadType(ref ReadOnlySpan<byte> data, int depth)
    {
        var kind = FuzzInput.ReadByte(ref data) % (depth >= 4 ? 13 : 22);
        switch (kind)
        {
            case 0: return ComponentValueType.Bool;
            case 1: return ComponentValueType.S8;
            case 2: return ComponentValueType.U8;
            case 3: return ComponentValueType.S16;
            case 4: return ComponentValueType.U16;
            case 5: return ComponentValueType.S32;
            case 6: return ComponentValueType.U32;
            case 7: return ComponentValueType.S64;
            case 8: return ComponentValueType.U64;
            case 9: return ComponentValueType.F32;
            case 10: return ComponentValueType.F64;
            case 11: return ComponentValueType.Char;
            case 12: return ComponentValueType.String;
            case 13: return new ComponentListType(ReadType(ref data, depth + 1));
            case 14: return new ComponentRecordType(ReadTypes(ref data, depth).Select((type, index) => new ComponentField("f" + index.ToString(System.Globalization.CultureInfo.InvariantCulture), type)));
            case 15: return new ComponentTupleType(ReadTypes(ref data, depth));
            case 16:
            {
                var count = (FuzzInput.ReadByte(ref data) % 4) + 1;
                var cases = new List<ComponentCase>();
                for (var i = 0; i < count; i++)
                {
                    var hasPayload = FuzzInput.ReadByte(ref data) % 2 == 0;
                    cases.Add(new ComponentCase("c" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), hasPayload ? ReadType(ref data, depth + 1) : null));
                }

                return new ComponentVariantType(cases);
            }

            case 17: return new ComponentEnumType(Labels("e", (FuzzInput.ReadByte(ref data) % 300) + 1));
            case 18: return new ComponentFlagsType(Labels("f", (FuzzInput.ReadByte(ref data) % 32) + 1));
            case 19: return new ComponentOptionType(ReadType(ref data, depth + 1));
            case 20:
            {
                var shape = FuzzInput.ReadByte(ref data);
                var ok = (shape & 1) != 0 ? ReadType(ref data, depth + 1) : null;
                var error = (shape & 2) != 0 ? ReadType(ref data, depth + 1) : null;
                return new ComponentResultType(ok, error);
            }

            default: return new ComponentListType(ReadType(ref data, depth + 1), (FuzzInput.ReadByte(ref data) % 8) + 1);
        }

        static IEnumerable<string> Labels(string prefix, int count) => Enumerable.Range(0, count).Select(index => prefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static ComponentValueType[] ReadTypes(ref ReadOnlySpan<byte> data, int depth)
    {
        var count = (FuzzInput.ReadByte(ref data) % 4) + 1;
        var types = new ComponentValueType[count];
        for (var i = 0; i < count; i++)
        {
            types[i] = ReadType(ref data, depth + 1);
        }

        return types;
    }

    private sealed class FuzzOutOfMemoryException : Exception
    {
        public FuzzOutOfMemoryException()
        {
        }

        public FuzzOutOfMemoryException(string message)
            : base(message)
        {
        }

        public FuzzOutOfMemoryException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    /// <summary>A memory and a realloc function that allocates after the previous allocation, and grows the memory.</summary>
    private static CanonicalOptions CreateOptions(WasmStore store, ComponentStringEncoding encoding, byte[]? content)
    {
        var memory = store.CreateMemory(new WasmMemoryType(1, 256));
        content?.AsSpan(0, Math.Min(content.Length, (int)WasmMemoryType.PageSize)).CopyTo(memory.GetSpan());
        var next = 1024UL;
        var realloc = store.CreateFunction(new WasmFunctionType([WasmValueType.I32, WasmValueType.I32, WasmValueType.I32, WasmValueType.I32], [WasmValueType.I32]), (arguments, results) =>
        {
            var oldPointer = (uint)arguments[0].I32;
            var oldLength = (uint)arguments[1].I32;
            var alignment = (uint)arguments[2].I32;
            var newLength = (uint)arguments[3].I32;
            var pointer = (next + alignment - 1) / alignment * alignment;
            next = pointer + newLength;
            while (next > (ulong)memory.GetSpan().Length)
            {
                try
                {
                    memory.Grow(1);
                }
                catch (WasmException)
                {
                    throw new FuzzOutOfMemoryException();
                }
            }

            var span = memory.GetSpan();
            span.Slice((int)oldPointer, (int)Math.Min(oldLength, newLength)).CopyTo(span[(int)pointer..]);
            results[0] = WasmValue.FromI32((int)pointer);
        });

        return new CanonicalOptions { Memory = memory, Realloc = realloc, StringEncoding = encoding };
    }

    private static void CanonicalAbiTarget(ReadOnlySpan<byte> data)
    {
        var type = ReadType(ref data, depth: 0);
        var encoding = (ComponentStringEncoding)(FuzzInput.ReadByte(ref data) % 3);
        using var store = Runtime.CreateStore();
        var componentStore = ((ManagedStore)store).Components;
        var instance = new ComponentInstanceState(componentStore);

        var source = new LiftLowerContext(LiftLowerDirection.Lift, CreateOptions(store, encoding, data.ToArray()), instance);
        ComponentValue value;
        try
        {
            value = CanonicalAbi.Load(source, 0, type);
        }
        catch (WasmTrapException)
        {
            // The memory is not a valid value of the type
            return;
        }

        // Lowered to another memory, and lifted again
        var target = new LiftLowerContext(LiftLowerDirection.Lower, CreateOptions(store, encoding, content: null), instance);
        ComponentValue copy;
        try
        {
            var pointer = target.Reallocate(0, 0, CanonicalAbi.Alignment(target, type), CanonicalAbi.ElemSize(target, type));
            CanonicalAbi.Store(target, value, type, pointer);
            copy = CanonicalAbi.Load(target.ReverseDirection(), pointer, type);
        }
        catch (FuzzOutOfMemoryException)
        {
            return;
        }

        FuzzAssert.True(value.Equals(copy), $"The value {FuzzAssert.Format(value.ToString())} is {FuzzAssert.Format(copy.ToString())} after it is stored and loaded");

        // Lowered to core values, and lifted again
        List<WasmValue> flat;
        try
        {
            flat = CanonicalAbi.LowerFlatValues(target, CanonicalAbi.MaxFlatParams, [value], [type]);
        }
        catch (FuzzOutOfMemoryException)
        {
            return;
        }

        var lifted = CanonicalAbi.LiftFlatValues(target.ReverseDirection(), CanonicalAbi.MaxFlatParams, new CanonicalAbi.CoreValueIterator(flat), [type]);
        FuzzAssert.True(value.Equals(lifted[0]), $"The value {FuzzAssert.Format(value.ToString())} is {FuzzAssert.Format(lifted[0].ToString())} after it is lowered and lifted");
    }

    // ------------------------------------------------------------------------------------------------------------
    // Seeds: the components of the unit tests (tests/Meziantou.Framework.WasmEngine.Tests/WasmComponentTests.cs)
    // ------------------------------------------------------------------------------------------------------------

    private static readonly (string Name, string Text)[] Components =
    [
        ("empty", "(component)"),
        ("greet", """
            (component
              (core module $M
                (memory (export "memory") 1)
                (global $heap (mut i32) (i32.const 1024))
                (func $realloc (export "realloc") (param i32 i32 i32 i32) (result i32)
                  (local $pointer i32)
                  (local.set $pointer (global.get $heap))
                  (global.set $heap (i32.add (global.get $heap) (local.get 3)))
                  (local.get $pointer))
                (data (i32.const 16) "Hello, ")
                (func (export "greet") (param $name i32) (param $length i32) (result i32)
                  (local $result i32)
                  (local.set $result (call $realloc (i32.const 0) (i32.const 0) (i32.const 1) (i32.add (local.get $length) (i32.const 7))))
                  (memory.copy (local.get $result) (i32.const 16) (i32.const 7))
                  (memory.copy (i32.add (local.get $result) (i32.const 7)) (local.get $name) (local.get $length))
                  (i32.store (i32.const 0) (local.get $result))
                  (i32.store (i32.const 4) (i32.add (local.get $length) (i32.const 7)))
                  (i32.const 0)))
              (core instance $m (instantiate $M))
              (func (export "greet") (param "name" string) (result string)
                (canon lift (core func $m "greet") (memory (core memory $m "memory")) (realloc (core func $m "realloc")))))
            """),
        ("add", """
            (component
              (import "add" (func $add (param "a" u32) (param "b" u32) (result u32)))
              (core func $add-core (canon lower (func $add)))
              (core module $M
                (import "host" "add" (func $add (param i32 i32) (result i32)))
                (func (export "run") (result i32)
                  (call $add (i32.const 40) (i32.const 2))))
              (core instance $m (instantiate $M (with "host" (instance (export "add" (func $add-core))))))
              (func (export "run") (result u32) (canon lift (core func $m "run"))))
            """),
        ("resources", """
            (component
              (type $counter (resource (rep i32)))
              (core func $new (canon resource.new $counter))
              (core func $rep (canon resource.rep $counter))
              (core func $drop (canon resource.drop $counter))
              (core module $M
                (import "" "new" (func $new (param i32) (result i32)))
                (import "" "rep" (func $rep (param i32) (result i32)))
                (import "" "drop" (func $drop (param i32)))
                (func (export "run") (result i32)
                  (local $handle i32)
                  (local.set $handle (call $new (i32.const 41)))
                  (call $rep (local.get $handle))
                  (call $drop (local.get $handle))
                  (i32.const 1)
                  i32.add))
              (core instance $m (instantiate $M (with "" (instance (export "new" (func $new)) (export "rep" (func $rep)) (export "drop" (func $drop))))))
              (func (export "run") (result u32) (canon lift (core func $m "run"))))
            """),
        ("values", """
            (component
              (core module $M
                (memory (export "memory") 1)
                (func (export "realloc") (param i32 i32 i32 i32) (result i32) (i32.const 1024))
                (func (export "echo") (param i32 i32 i32 i32 i32) (result i32)
                  (i32.store (i32.const 8) (local.get 0))
                  (i32.const 8)))
              (core instance $m (instantiate $M))
              (type $t (record (field "a" u32) (field "b" string) (field "c" (list u8))))
              (func (export "echo") (param "value" $t) (result (tuple u32 (option (result f64 (error char))) (flags "a" "b") (enum "x" "y")))
                (canon lift (core func $m "echo") (memory (core memory $m "memory")) (realloc (core func $m "realloc")))))
            """),
        ("trap", """
            (component
              (core module $M
                (func (export "fail") unreachable)
                (func (export "ok")))
              (core instance $m (instantiate $M))
              (func (export "ok") (canon lift (core func $m "ok")))
              (func (export "fail") (canon lift (core func $m "fail"))))
            """),
        ("interface", """
            (component
              (core module $M (func (export "f") (result i32) (i32.const 7)))
              (core instance $m (instantiate $M))
              (func $f (result u32) (canon lift (core func $m "f")))
              (instance $i (export "f" (func $f)))
              (export "test:demo/api@1.0.0" (instance $i)))
            """),
        ("async", """
            (component
              (core module $M
                (import "" "task.return" (func $task.return (param i32)))
                (func (export "run") (result i32)
                  (call $task.return (i32.const 42))
                  (i32.const 0))
                (func (export "callback") (param i32 i32 i32) (result i32) unreachable))
              (core func $task.return (canon task.return (result u32)))
              (core instance $m (instantiate $M (with "" (instance (export "task.return" (func $task.return))))))
              (func (export "run") async (result u32) (canon lift (core func $m "run") async (callback (core func $m "callback")))))
            """),
        ("validation-names", """
            (component
              (import "wasi:io/streams@0.2.0" (instance
                (export "input-stream" (type $input (sub resource)))
                (export "output-stream" (type $output (sub resource)))
                (export "[method]input-stream.subscribe" (func (param "self" (borrow $input))))
                (export "[method]output-stream.subscribe" (func (param "self" (borrow $output))))
                (export "[static]output-stream.create" (func (result (own $output))))))
              (import "a:b/c@1" (version-suffix ".2.3") (instance))
              (import "my-store" (implements "wasi:keyvalue/store@0.2.0") (external-id "id") (instance)))
            """),
        ("validation-instantiation", """
            (component
              (import "t1" (type $t1 (sub resource)))
              (import "t2" (type $t2 (sub resource)))
              (component $c (import "a" (type $a (sub resource))) (import "b" (type (eq $a))))
              (instance (instantiate $c (with "a" (type $t1)) (with "b" (type $t2)))))
            """),
        ("validation-visibility", """
            (component
              (core module $M (func (export "f") (result i32) (i32.const 0)))
              (core instance $m (instantiate $M))
              (type $point (record (field "x" u32)))
              (export $point-export "point" (type $point))
              (func $f (result $point-export) (canon lift (core func $m "f")))
              (export "f" (func $f)))
            """),
    ];
}
