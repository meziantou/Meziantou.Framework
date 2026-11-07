using Meziantou.Framework.WebPlatform.WebAssembly;

namespace Meziantou.Framework.WebPlatform.Wasmtime;

/// <summary>The modules that implement the operations the C API of Wasmtime does not provide.</summary>
internal static class WasmtimeModules
{
    /// <summary>
    /// The conversions of the references between the any and the extern hierarchies:
    /// <code>
    /// (module
    ///   (func (export "any") (param externref) (result anyref) (any.convert_extern (local.get 0)))
    ///   (func (export "extern") (param anyref) (result externref) (extern.convert_any (local.get 0))))
    /// </code>
    /// </summary>
    public static byte[] Conversions =>
    [
        0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,

        // type section: (func (param externref) (result anyref)), (func (param anyref) (result externref))
        0x01, 0x0B, 0x02, 0x60, 0x01, 0x6F, 0x01, 0x6E, 0x60, 0x01, 0x6E, 0x01, 0x6F,

        // function section
        0x03, 0x03, 0x02, 0x00, 0x01,

        // export section: "any" (func 0), "extern" (func 1)
        0x07, 0x10, 0x02, 0x03, (byte)'a', (byte)'n', (byte)'y', 0x00, 0x00, 0x06, (byte)'e', (byte)'x', (byte)'t', (byte)'e', (byte)'r', (byte)'n', 0x00, 0x01,

        // code section: local.get 0, any.convert_extern (0xFB 26) / extern.convert_any (0xFB 27), end
        0x0A, 0x0F, 0x02, 0x06, 0x00, 0x20, 0x00, 0xFB, 0x1A, 0x0B, 0x06, 0x00, 0x20, 0x00, 0xFB, 0x1B, 0x0B,
    ];

    /// <summary>
    /// A module that exports a table, for the tables the C API cannot create (64-bit addresses):
    /// <c>(module (table (export "t") i64 min max reftype))</c>. The elements are null.
    /// </summary>
    public static byte[] CreateTableModule(WasmTableType type)
    {
        var table = new List<byte> { 0x01 }; // one table
        table.AddRange(EncodeReferenceType(type.ElementType));
        table.Add((byte)((type.Is64 ? 0x04 : 0x00) | (type.Maximum.HasValue ? 0x01 : 0x00)));
        WriteU64(table, type.Minimum);
        if (type.Maximum is { } maximum)
        {
            WriteU64(table, maximum);
        }

        var module = new List<byte> { 0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00 };
        AddSection(module, 0x04, table);
        AddSection(module, 0x07, [0x01, 0x01, (byte)'t', 0x01, 0x00]); // export "t" (table 0)
        return [.. module];
    }

    /// <summary>
    /// A module that exports a memory: <c>(module (memory (export "m") i64 min max))</c>. The memories created by the C API
    /// (<c>wasmtime_memory_new</c>) are not allocated by the memory creator of the engine, unlike the memories of the instances.
    /// </summary>
    public static byte[] CreateMemoryModule(WasmMemoryType type)
    {
        var memory = new List<byte> { 0x01 }; // one memory
        memory.Add((byte)((type.Is64 ? 0x04 : 0x00) | (type.Maximum.HasValue ? 0x01 : 0x00)));
        WriteU64(memory, type.Minimum);
        if (type.Maximum is { } maximum)
        {
            WriteU64(memory, maximum);
        }

        var module = new List<byte> { 0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00 };
        AddSection(module, 0x05, memory);
        AddSection(module, 0x07, [0x01, 0x01, (byte)'m', 0x02, 0x00]); // export "m" (memory 0)
        return [.. module];
    }

    private static void AddSection(List<byte> module, byte id, List<byte> contents)
    {
        module.Add(id);
        WriteU64(module, (ulong)contents.Count);
        module.AddRange(contents);
    }

    /// <summary>The encoding of a reference type of an abstract heap type (https://webassembly.github.io/spec/core/binary/types.html#reference-types).</summary>
    private static byte[] EncodeReferenceType(WasmValueType type)
    {
        var heapType = type.Heap switch
        {
            WasmHeapKind.NoExn => (byte)0x74,
            WasmHeapKind.NoFunc => (byte)0x73,
            WasmHeapKind.NoExtern => (byte)0x72,
            WasmHeapKind.None => (byte)0x71,
            WasmHeapKind.Func => (byte)0x70,
            WasmHeapKind.Extern => (byte)0x6F,
            WasmHeapKind.Any => (byte)0x6E,
            WasmHeapKind.Eq => (byte)0x6D,
            WasmHeapKind.I31 => (byte)0x6C,
            WasmHeapKind.Struct => (byte)0x6B,
            WasmHeapKind.Array => (byte)0x6A,
            WasmHeapKind.Exn => (byte)0x69,
            _ => throw new NotSupportedException("A concrete heap type cannot be encoded"),
        };

        return [type.Nullable ? (byte)0x63 : (byte)0x64, heapType];
    }

    private static void WriteU64(List<byte> output, ulong value)
    {
        do
        {
            var b = (byte)(value & 0x7F);
            value >>= 7;
            output.Add(value == 0 ? b : (byte)(b | 0x80));
        }
        while (value != 0);
    }
}
