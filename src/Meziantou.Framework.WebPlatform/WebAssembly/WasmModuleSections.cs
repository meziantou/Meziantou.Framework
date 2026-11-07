using System.Text;

namespace Meziantou.Framework.WebPlatform.WebAssembly;

/// <summary>
/// The sections of a valid module that the runtimes do not expose (https://webassembly.github.io/spec/core/binary/modules.html):
/// the custom sections, the function indices of the exports, which are the names of the Exported Functions
/// (https://webassembly.github.io/spec/js-api/#name-of-the-webassembly-function), and the address types and the limits of
/// the tables.
/// </summary>
internal sealed class WasmModuleSections
{
    private const byte CustomSectionId = 0;
    private const byte ImportSectionId = 2;
    private const byte TableSectionId = 4;
    private const byte ExportSectionId = 7;

    private readonly List<(string Name, int Offset, int Length)> _customSections = [];
    private readonly Dictionary<string, (byte Kind, uint Index)> _exports = new(StringComparer.Ordinal);
    private readonly List<WasmTableLimits?> _tables = [];
    private readonly List<WasmTableLimits?> _importedTables = [];
    private readonly List<byte> _importKinds = [];
    private bool _importsRead = true;

    private WasmModuleSections()
    {
    }

    /// <summary>The custom sections: their name, and the range of their contents in the bytes of the module.</summary>
    public IReadOnlyList<(string Name, int Offset, int Length)> CustomSections => _customSections;

    /// <summary>Returns the index of an exported function in the function index space of the module, or null.</summary>
    public uint? GetExportedFunctionIndex(string name) => _exports.TryGetValue(name, out var export) && export.Kind == 0 ? export.Index : null;

    /// <summary>Returns the limits of an exported table, or null when they cannot be read.</summary>
    public WasmTableLimits? GetExportedTable(string name) => _exports.TryGetValue(name, out var export) && export.Kind == 1 && export.Index < _tables.Count ? _tables[(int)export.Index] : null;

    /// <summary>
    /// Returns the index (in the imports of the module) of the import that an export re-exports (e.g. an imported function
    /// that is exported), or null when the export is defined by the module: the export is the same external value.
    /// </summary>
    public int? GetReexportedImport(string name)
    {
        if (!_importsRead || !_exports.TryGetValue(name, out var export))
            return null;

        var count = 0u;
        for (var i = 0; i < _importKinds.Count; i++)
        {
            if (_importKinds[i] != export.Kind)
                continue;

            if (count == export.Index)
                return i;

            count++;
        }

        return null;
    }

    /// <summary>Returns the limits of the n-th imported table, or null when they cannot be read.</summary>
    public WasmTableLimits? GetImportedTable(int tableImportIndex) => tableImportIndex < _importedTables.Count ? _importedTables[tableImportIndex] : null;

    /// <summary>Reads the sections of a valid module (the parts that cannot be read are ignored).</summary>
    public static WasmModuleSections Read(ReadOnlySpan<byte> bytes)
    {
        var result = new WasmModuleSections();
        var position = 8; // magic and version
        while (position < bytes.Length)
        {
            var id = bytes[position++];
            if (!TryReadU32(bytes, ref position, out var size) || size > (uint)(bytes.Length - position))
                break;

            var end = position + (int)size;
            var section = bytes[..end];
            var sectionPosition = position;
            switch (id)
            {
                case CustomSectionId:
                    if (TryReadName(section, ref sectionPosition, out var name))
                    {
                        result._customSections.Add((name, sectionPosition, end - sectionPosition));
                    }

                    break;

                case ImportSectionId:
                    result.ReadImports(section, sectionPosition);
                    break;

                case TableSectionId:
                    result.ReadTables(section, sectionPosition);
                    break;

                case ExportSectionId:
                    result.ReadExports(section, sectionPosition);
                    break;
            }

            position = end;
        }

        return result;
    }

    private void ReadImports(ReadOnlySpan<byte> bytes, int position)
    {
        // The import kinds are only used when all the imports are read
        _importsRead = false;
        if (!TryReadU32(bytes, ref position, out var count))
            return;

        for (var i = 0u; i < count; i++)
        {
            if (!TryReadName(bytes, ref position, out _) || !TryReadName(bytes, ref position, out _) || position >= bytes.Length)
                return;

            var kind = bytes[position++];
            _importKinds.Add(kind);
            switch (kind)
            {
                case 0x00: // func typeidx
                    if (!TryReadU32(bytes, ref position, out _))
                        return;

                    break;

                case 0x01: // table tabletype
                    var table = TryReadTableType(bytes, ref position);
                    if (table is null)
                        return;

                    _tables.Add(table);
                    _importedTables.Add(table);
                    break;

                case 0x02: // mem memtype
                    if (!TrySkipLimits(bytes, ref position))
                        return;

                    break;

                case 0x03: // global globaltype
                    if (!TrySkipValueType(bytes, ref position) || position >= bytes.Length)
                        return;

                    position++; // mut
                    break;

                case 0x04: // tag: attribute, typeidx
                    if (position >= bytes.Length)
                        return;

                    position++;
                    if (!TryReadU32(bytes, ref position, out _))
                        return;

                    break;

                default:
                    return;
            }
        }

        _importsRead = true;
    }

    private void ReadTables(ReadOnlySpan<byte> bytes, int position)
    {
        if (!TryReadU32(bytes, ref position, out var count))
            return;

        for (var i = 0u; i < count; i++)
        {
            // table ::= tt:tabletype | 0x40 0x00 tt:tabletype e:expr
            var hasInitializer = position + 1 < bytes.Length && bytes[position] == 0x40 && bytes[position + 1] == 0x00;
            if (hasInitializer)
            {
                position += 2;
            }

            var table = TryReadTableType(bytes, ref position);
            if (table is null)
                return;

            _tables.Add(table);
            if (hasInitializer && !TrySkipConstantExpression(bytes, ref position))
                return;
        }
    }

    private void ReadExports(ReadOnlySpan<byte> bytes, int position)
    {
        if (!TryReadU32(bytes, ref position, out var count))
            return;

        for (var i = 0u; i < count; i++)
        {
            if (!TryReadName(bytes, ref position, out var name) || position >= bytes.Length)
                return;

            var kind = bytes[position++];
            if (!TryReadU32(bytes, ref position, out var index))
                return;

            _exports.TryAdd(name, (kind, index));
        }
    }

    /// <summary>tabletype ::= et:reftype lim:limits</summary>
    private static WasmTableLimits? TryReadTableType(ReadOnlySpan<byte> bytes, ref int position)
    {
        if (!TrySkipValueType(bytes, ref position) || position >= bytes.Length)
            return null;

        // limits ::= 0x00 n | 0x01 n m (i32), 0x04 n | 0x05 n m (i64)
        var flags = bytes[position++];
        var is64 = (flags & 0x04) != 0;
        if (!TryReadU64(bytes, ref position, out var minimum))
            return null;

        ulong? maximum = null;
        if ((flags & 0x01) != 0)
        {
            if (!TryReadU64(bytes, ref position, out var max))
                return null;

            maximum = max;
        }

        return new WasmTableLimits(minimum, maximum, is64);
    }

    private static bool TrySkipLimits(ReadOnlySpan<byte> bytes, ref int position)
    {
        if (position >= bytes.Length)
            return false;

        var flags = bytes[position++];
        if (!TryReadU64(bytes, ref position, out _))
            return false;

        if ((flags & 0x01) != 0 && !TryReadU64(bytes, ref position, out _))
            return false;

        // custom page sizes: the log2 of the page size
        return (flags & 0x08) == 0 || TryReadU32(bytes, ref position, out _);
    }

    /// <summary>valtype ::= numtype | vectype | reftype (with the abbreviations of the reference types)</summary>
    private static bool TrySkipValueType(ReadOnlySpan<byte> bytes, ref int position)
    {
        if (position >= bytes.Length)
            return false;

        var b = bytes[position++];

        // reftype ::= 0x63 ht (ref null ht) | 0x64 ht (ref ht)
        return b is not (0x63 or 0x64) || TrySkipS33(bytes, ref position);
    }

    /// <summary>Skips a constant expression (https://webassembly.github.io/spec/core/valid/instructions.html#constant-expressions) up to its <c>end</c>.</summary>
    private static bool TrySkipConstantExpression(ReadOnlySpan<byte> bytes, ref int position)
    {
        while (position < bytes.Length)
        {
            var opcode = bytes[position++];
            switch (opcode)
            {
                case 0x0B: // end
                    return true;
                case 0x41: // i32.const
                case 0x42: // i64.const
                    if (!TrySkipS33(bytes, ref position))
                        return false;

                    break;
                case 0x43: // f32.const
                    position += 4;
                    break;
                case 0x44: // f64.const
                    position += 8;
                    break;
                case 0x23: // global.get
                case 0xD2: // ref.func
                    if (!TryReadU32(bytes, ref position, out _))
                        return false;

                    break;
                case 0xD0: // ref.null ht
                    if (!TrySkipS33(bytes, ref position))
                        return false;

                    break;
                case 0x6A or 0x6B or 0x6C or 0x7C or 0x7D or 0x7E: // extended constant expressions
                    break;
                case 0xFB: // GC instructions
                    if (!TryReadU32(bytes, ref position, out var gcOpcode))
                        return false;

                    // struct.new, struct.new_default, array.new, array.new_default: typeidx; array.new_fixed: typeidx, n
                    if (gcOpcode is 0 or 1 or 6 or 7 or 8 && !TryReadU32(bytes, ref position, out _))
                        return false;

                    if (gcOpcode == 8 && !TryReadU32(bytes, ref position, out _))
                        return false;

                    break;
                case 0xFD: // v128.const
                    if (!TryReadU32(bytes, ref position, out var simdOpcode) || simdOpcode != 12)
                        return false;

                    position += 16;
                    break;
                default:
                    return false;
            }
        }

        return false;
    }

    private static bool TryReadName(ReadOnlySpan<byte> bytes, ref int position, out string name)
    {
        name = "";
        if (!TryReadU32(bytes, ref position, out var length) || length > (uint)(bytes.Length - position))
            return false;

        name = Encoding.UTF8.GetString(bytes.Slice(position, (int)length));
        position += (int)length;
        return true;
    }

    /// <summary>Reads an unsigned LEB128 integer of at most 32 bits (https://webassembly.github.io/spec/core/binary/values.html#integers).</summary>
    private static bool TryReadU32(ReadOnlySpan<byte> bytes, ref int position, out uint value)
    {
        var result = TryReadU64(bytes, ref position, out var value64) && value64 <= uint.MaxValue;
        value = (uint)value64;
        return result;
    }

    private static bool TryReadU64(ReadOnlySpan<byte> bytes, ref int position, out ulong value)
    {
        value = 0;
        for (var shift = 0; shift < 70; shift += 7)
        {
            if (position >= bytes.Length)
                return false;

            var b = bytes[position++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return true;
        }

        return false;
    }

    /// <summary>Skips a signed LEB128 integer (s33, s32, s64).</summary>
    private static bool TrySkipS33(ReadOnlySpan<byte> bytes, ref int position)
    {
        for (var i = 0; i < 10; i++)
        {
            if (position >= bytes.Length)
                return false;

            if ((bytes[position++] & 0x80) == 0)
                return true;
        }

        return false;
    }
}

/// <summary>The address type and the limits of a table, read from the binary of a module.</summary>
internal sealed record WasmTableLimits(ulong Minimum, ulong? Maximum, bool Is64);
