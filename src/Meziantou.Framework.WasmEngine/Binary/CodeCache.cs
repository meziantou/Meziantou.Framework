using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Meziantou.Framework.WasmEngine;

/// <summary>
/// The code cache of a module (<see cref="WasmModule.CreateCodeCache"/>): the validated and compiled code of its functions,
/// so that a later compilation of the same binary skips the validation and the compilation of the code section.
/// </summary>
/// <remarks>
/// <para>
/// The layout: a header (the magic number, the version of the format, the identity of the build of the engine, the
/// flags, the SHA-256 hash of the binary of the module), the code of each defined function, and the SHA-256 hash of the
/// preceding bytes. The numbers are LEB128 integers (zigzag for the signed ones). An instruction is its opcode, a byte
/// whose bits tell which operands are not zero, and these operands.
/// </para>
/// <para>
/// The other sections of the module are decoded again from its binary: the types are canonicalized by the registry of
/// the runtime, and the concrete heap types of the code are written as indices of the types of the module.
/// </para>
/// </remarks>
internal static class CodeCache
{
    private const int FormatVersion = 1;
    private const int EngineIdSize = 16;

    /// <summary>The size of the SHA-256 hashes: the hash of the binary in the header, and the hash of the cache at its end.</summary>
    internal const int HashSize = 32;

    /// <summary>The size of the header: the code of the functions follows it.</summary>
    internal const int HeaderSize = 4 + 4 + EngineIdSize + 1 + HashSize;
    private const byte FlagHeights = 1;

    private const byte HasI0 = 1;
    private const byte HasI1 = 2;
    private const byte HasI2 = 4;
    private const byte HasL0 = 8;
    private const byte HasL1 = 16;
    private const byte HasType = 32;

    private static ReadOnlySpan<byte> Magic => "MFWC"u8;

    /// <summary>The identity of the build of the engine: the opcodes and the layout of the code change between builds.</summary>
    private static readonly byte[] EngineId = GetEngineId();

    /// <summary>Whether the compiler records the heights of the operand stack (used by the compilation to .NET code).</summary>
    private static bool RecordHeights => System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported;

    private static byte[] GetEngineId()
    {
        try
        {
            return typeof(CodeCache).Module.ModuleVersionId.ToByteArray();
        }
#pragma warning disable CA1031 // A platform without the identity of the module uses the version of the assembly
        catch (Exception)
#pragma warning restore CA1031
        {
            var version = typeof(CodeCache).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
            return SHA256.HashData(Encoding.UTF8.GetBytes(version))[..EngineIdSize];
        }
    }

    /// <summary>Writes the code cache of a module.</summary>
    /// <exception cref="NotSupportedException">The code references a type that is not a type of the module.</exception>
    public static byte[] Serialize(ModuleInfo module, byte[] moduleBytes)
    {
        var typeIndices = new Dictionary<DefinedType, int>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < module.Types.Count; i++)
        {
            typeIndices.TryAdd(module.Types[i], i);
        }

        var writer = new Writer(HeaderSize + (module.Code.Sum(code => code.Code.Length) * 6));
        writer.WriteBytes(Magic);
        writer.WriteInt32(FormatVersion);
        writer.WriteBytes(EngineId);
        writer.WriteByte(RecordHeights ? FlagHeights : (byte)0);
        Span<byte> moduleHash = stackalloc byte[HashSize];
        SHA256.HashData(moduleBytes, moduleHash);
        writer.WriteBytes(moduleHash);

        writer.WriteUInt((uint)module.Code.Count);
        foreach (var code in module.Code)
        {
            WriteCode(ref writer, code, typeIndices);
        }

        Span<byte> hash = stackalloc byte[HashSize];
        SHA256.HashData(writer.WrittenSpan, hash);
        writer.WriteBytes(hash);
        return writer.ToArray();
    }

    /// <summary>Returns whether a code cache was written by this build of the engine for this binary, and is not corrupted.</summary>
    public static bool IsValid(byte[] moduleBytes, ReadOnlySpan<byte> cache)
    {
        if (cache.Length < HeaderSize + HashSize || !cache.StartsWith(Magic))
            return false;

        if (BinaryPrimitives.ReadInt32LittleEndian(cache[4..]) != FormatVersion || !cache.Slice(8, EngineIdSize).SequenceEqual(EngineId))
            return false;

        if (cache[8 + EngineIdSize] != (RecordHeights ? FlagHeights : 0))
            return false;

        Span<byte> hash = stackalloc byte[HashSize];
        SHA256.HashData(moduleBytes, hash);
        if (!cache.Slice(8 + EngineIdSize + 1, HashSize).SequenceEqual(hash))
            return false;

        SHA256.HashData(cache[..^HashSize], hash);
        return cache[^HashSize..].SequenceEqual(hash);
    }

    /// <summary>Reads the code of the defined functions of a module decoded without its code section (<see cref="ModuleDecoder.DecodeWithoutCode"/>).</summary>
    /// <exception cref="CodeCacheFormatException">The cache is malformed or does not match the module.</exception>
    public static void ReadCode(ReadOnlySpan<byte> cache, ModuleInfo module) => ReadPayload(cache[HeaderSize..^HashSize], module);

    /// <summary>Reads the code of the defined functions of a module: the bytes of a cache between its header and its hash.</summary>
    /// <exception cref="CodeCacheFormatException">The code is malformed or does not match the module.</exception>
    internal static void ReadPayload(ReadOnlySpan<byte> payload, ModuleInfo module)
    {
        var reader = new Reader(payload);
        var count = reader.ReadCount();
        if (count != module.Functions.Count - module.ImportedFunctionCount)
            throw new CodeCacheFormatException("The number of functions does not match the module");

        for (var i = 0; i < count; i++)
        {
            var functionIndex = module.ImportedFunctionCount + i;
            var code = ReadCode(ref reader, module, module.Functions[functionIndex]);
            code.FunctionIndex = functionIndex;
            module.Code.Add(code);
        }

        if (!reader.AtEnd)
            throw new CodeCacheFormatException("Unexpected data after the code");
    }

    private static void WriteCode(ref Writer writer, CompiledCode code, Dictionary<DefinedType, int> typeIndices)
    {
        writer.WriteUInt((uint)code.MaxStackHeight);
        writer.WriteUInt((uint)code.Locals.Length);
        foreach (var local in code.Locals)
        {
            WriteValType(ref writer, local, typeIndices);
        }

        writer.WriteUInt((uint)code.Code.Length);
        foreach (ref readonly var instr in code.Code.AsSpan())
        {
            writer.WriteUInt(instr.Op);
            var flags = (byte)((instr.I0 != 0 ? HasI0 : 0) | (instr.I1 != 0 ? HasI1 : 0) | (instr.I2 != 0 ? HasI2 : 0) | (instr.L0 != 0 ? HasL0 : 0) | (instr.L1 != 0 ? HasL1 : 0) | (instr.O is not null ? HasType : 0));
            writer.WriteByte(flags);
            if (instr.I0 != 0)
            {
                writer.WriteInt(instr.I0);
            }

            if (instr.I1 != 0)
            {
                writer.WriteInt(instr.I1);
            }

            if (instr.I2 != 0)
            {
                writer.WriteInt(instr.I2);
            }

            if (instr.L0 != 0)
            {
                writer.WriteLong(instr.L0);
            }

            if (instr.L1 != 0)
            {
                writer.WriteLong(instr.L1);
            }

            if (instr.O is not null)
            {
                writer.WriteUInt((uint)GetTypeIndex(instr.O as DefinedType ?? throw new NotSupportedException("The code references an object that is not a type"), typeIndices));
            }
        }

        writer.WriteUInt((uint)code.BranchTables.Length);
        foreach (var target in code.BranchTables)
        {
            writer.WriteInt(target.Pc);
            writer.WriteInt(target.Arity);
            writer.WriteInt(target.Height);
        }

        writer.WriteUInt((uint)code.Handlers.Length);
        foreach (var handler in code.Handlers)
        {
            writer.WriteInt(handler.Start);
            writer.WriteInt(handler.End);
            writer.WriteUInt((uint)handler.Catches.Length);
            foreach (var clause in handler.Catches)
            {
                writer.WriteByte((byte)clause.Kind);
                writer.WriteInt(clause.Tag);
                writer.WriteInt(clause.Pc);
                writer.WriteInt(clause.Arity);
                writer.WriteInt(clause.Height);
            }
        }

        WriteInts(ref writer, code.Heights);
        WriteInts(ref writer, code.Pops);
    }

    private static CompiledCode ReadCode(ref Reader reader, ModuleInfo module, DefinedType type)
    {
        var maxStackHeight = reader.ReadCount();
        var locals = new ValType[reader.ReadCount()];
        for (var i = 0; i < locals.Length; i++)
        {
            locals[i] = ReadValType(ref reader, module);
        }

        if (locals.Length < type.Parameters.Length)
            throw new CodeCacheFormatException("The locals do not match the type of the function");

        var code = new Instr[reader.ReadCount()];
        for (var i = 0; i < code.Length; i++)
        {
            ref var instr = ref code[i];
            var op = reader.ReadUInt();
            if (op > ushort.MaxValue)
                throw new CodeCacheFormatException("Invalid opcode");

            instr.Op = (ushort)op;
            var flags = reader.ReadByte();
            if ((flags & HasI0) != 0)
            {
                instr.I0 = reader.ReadInt();
            }

            if ((flags & HasI1) != 0)
            {
                instr.I1 = reader.ReadInt();
            }

            if ((flags & HasI2) != 0)
            {
                instr.I2 = reader.ReadInt();
            }

            if ((flags & HasL0) != 0)
            {
                instr.L0 = reader.ReadLong();
            }

            if ((flags & HasL1) != 0)
            {
                instr.L1 = reader.ReadLong();
            }

            if ((flags & HasType) != 0)
            {
                instr.O = ReadType(ref reader, module);
            }
        }

        var branchTables = new BranchTarget[reader.ReadCount()];
        for (var i = 0; i < branchTables.Length; i++)
        {
            branchTables[i] = new BranchTarget { Pc = reader.ReadInt(), Arity = reader.ReadInt(), Height = reader.ReadInt() };
        }

        var handlers = new HandlerInfo[reader.ReadCount()];
        for (var i = 0; i < handlers.Length; i++)
        {
            var start = reader.ReadInt();
            var end = reader.ReadInt();
            var catches = new CatchClause[reader.ReadCount()];
            for (var j = 0; j < catches.Length; j++)
            {
                var kind = reader.ReadByte();
                if (kind > (byte)CatchKind.CatchAllRef)
                    throw new CodeCacheFormatException("Invalid catch clause");

                catches[j] = new CatchClause { Kind = (CatchKind)kind, Tag = reader.ReadInt(), Pc = reader.ReadInt(), Arity = reader.ReadInt(), Height = reader.ReadInt() };
            }

            handlers[i] = new HandlerInfo { Start = start, End = end, Catches = catches };
        }

        return new CompiledCode
        {
            Code = code,
            BranchTables = branchTables,
            Handlers = handlers,
            Locals = locals,
            MaxStackHeight = maxStackHeight,
            ParameterCount = type.Parameters.Length,
            Results = type.Results,
            Heights = ReadInts(ref reader),
            Pops = ReadInts(ref reader),
        };
    }

    private static void WriteInts(ref Writer writer, int[] values)
    {
        writer.WriteUInt((uint)values.Length);
        foreach (var value in values)
        {
            writer.WriteInt(value);
        }
    }

    private static int[] ReadInts(ref Reader reader)
    {
        var values = new int[reader.ReadCount()];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = reader.ReadInt();
        }

        return values;
    }

    private static int GetTypeIndex(DefinedType type, Dictionary<DefinedType, int> typeIndices)
        => typeIndices.TryGetValue(type, out var index) ? index : throw new NotSupportedException("The code references a type that is not a type of the module");

    private static DefinedType ReadType(ref Reader reader, ModuleInfo module)
    {
        var index = reader.ReadUInt();
        if (index >= (uint)module.Types.Count)
            throw new CodeCacheFormatException("Invalid type index");

        return module.Types[(int)index];
    }

    private static void WriteValType(ref Writer writer, ValType type, Dictionary<DefinedType, int> typeIndices)
    {
        writer.WriteByte((byte)type.Code);
        if (type.Code != ValueCode.Ref)
            return;

        writer.WriteByte(type.Nullable ? (byte)1 : (byte)0);
        if (type.Defined is { } defined)
        {
            writer.WriteUInt((uint)GetTypeIndex(defined, typeIndices) + 1);
        }
        else
        {
            writer.WriteUInt(0);
            writer.WriteByte((byte)type.Heap);
        }
    }

    private static ValType ReadValType(ref Reader reader, ModuleInfo module)
    {
        var code = reader.ReadByte();
        if (code > (byte)ValueCode.Bottom)
            throw new CodeCacheFormatException("Invalid value type");

        if ((ValueCode)code != ValueCode.Ref)
            return new ValType((ValueCode)code);

        var nullable = reader.ReadByte() != 0;
        var index = reader.ReadUInt();
        if (index == 0)
            return new ValType(nullable, (WasmHeapKind)reader.ReadByte());

        if (index > (uint)module.Types.Count)
            throw new CodeCacheFormatException("Invalid type index");

        return new ValType(nullable, module.Types[(int)index - 1]);
    }

    private struct Writer(int capacity)
    {
        private byte[] _buffer = new byte[Math.Max(capacity, 64)];
        private int _length;

        public readonly ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _length);

        public readonly byte[] ToArray() => _buffer.AsSpan(0, _length).ToArray();

        public void WriteByte(byte value)
        {
            Ensure(1);
            _buffer[_length++] = value;
        }

        public void WriteBytes(ReadOnlySpan<byte> value)
        {
            Ensure(value.Length);
            value.CopyTo(_buffer.AsSpan(_length));
            _length += value.Length;
        }

        public void WriteInt32(int value)
        {
            Ensure(4);
            BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(_length), value);
            _length += 4;
        }

        public void WriteUInt(uint value) => WriteULong(value);

        public void WriteInt(int value) => WriteULong((uint)((value << 1) ^ (value >> 31)));

        public void WriteLong(long value) => WriteULong((ulong)((value << 1) ^ (value >> 63)));

        private void WriteULong(ulong value)
        {
            Ensure(10);
            while (value >= 0x80)
            {
                _buffer[_length++] = (byte)(value | 0x80);
                value >>= 7;
            }

            _buffer[_length++] = (byte)value;
        }

        private void Ensure(int count)
        {
            if (_length + count > _buffer.Length)
            {
                Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _length + count));
            }
        }
    }

    private ref struct Reader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _position;

        public readonly bool AtEnd => _position == _data.Length;

        public byte ReadByte()
        {
            if (_position >= _data.Length)
                throw new CodeCacheFormatException("Unexpected end of the cache");

            return _data[_position++];
        }

        public uint ReadUInt()
        {
            var value = ReadULong();
            if (value > uint.MaxValue)
                throw new CodeCacheFormatException("Invalid number");

            return (uint)value;
        }

        /// <summary>Reads the number of items of a list: each item has at least one byte.</summary>
        public int ReadCount()
        {
            var value = ReadUInt();
            if (value > (uint)(_data.Length - _position))
                throw new CodeCacheFormatException("Invalid count");

            return (int)value;
        }

        public int ReadInt()
        {
            var value = ReadUInt();
            return (int)(value >> 1) ^ -(int)(value & 1);
        }

        public long ReadLong()
        {
            var value = ReadULong();
            return (long)(value >> 1) ^ -(long)(value & 1);
        }

        private ulong ReadULong()
        {
            ulong result = 0;
            for (var shift = 0; shift < 70; shift += 7)
            {
                var b = ReadByte();
                if (shift == 63 && b > 1)
                    throw new CodeCacheFormatException("Invalid number");

                result |= (ulong)(b & 0x7F) << shift;
                if (b < 0x80)
                    return result;
            }

            throw new CodeCacheFormatException("Invalid number");
        }
    }
}

/// <summary>A code cache is malformed or does not match its module: the module is compiled from its binary.</summary>
#pragma warning disable CA1032, CA1064 // Internal: the module is compiled from its binary instead
internal sealed class CodeCacheFormatException(string message) : Exception(message);
#pragma warning restore CA1032, CA1064
