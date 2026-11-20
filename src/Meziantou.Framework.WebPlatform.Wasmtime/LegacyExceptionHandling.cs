using System.Buffers;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Wasmtime;

/// <summary>
/// Translates the legacy exception handling instructions of WebAssembly (<c>try</c>, <c>catch</c>, <c>catch_all</c>,
/// <c>rethrow</c>, <c>delegate</c>), which Wasmtime does not support, to the exception references of WebAssembly 3.0
/// (<c>try_table</c>, <c>throw_ref</c>), as the <c>translate-to-exnref</c> pass of Binaryen does. The toolchains still
/// emit the legacy instructions by default (Emscripten, and the runtime of .NET in the browser), and the browsers run them.
/// </summary>
/// <remarks>
/// <para>A <c>try</c> with catch clauses becomes blocks around a <c>try_table</c>, one block per clause:</para>
/// <code>
/// try (param P) (result T)          block $outer (param P) (result T)
///   body                              block $c1 (param P) (result exnref)
/// catch $tag                            block $c0 (param P) (result tag-params exnref)
///   handler0                              try_table (param P) (result T) (catch_ref $tag $c0) (catch_all_ref $c1)
/// catch_all                                 body
///   handler1                              end
/// end                                     br $outer
///                                       end
///                                       local.set $exn handler0 br $outer
///                                     end
///                                     local.set $exn handler1
///                                   end
/// </code>
/// <para>
/// The exception of a clause is stored in a local, which a <c>rethrow</c> of the clause throws again (<c>throw_ref</c>).
/// A <c>try</c> ... <c>delegate $l</c> becomes a <c>try_table</c> whose <c>catch_all_ref</c> branches to a block at the
/// start of the code of the label <c>$l</c> (the body of a block, a branch of an <c>if</c>, the body or a handler of a
/// <c>try</c>), which throws the exception again there: the handlers that enclose this code receive it, as if it was
/// thrown by the instructions of the label.
/// </para>
/// <para>
/// The functions without legacy instructions are copied as they are. The translation does not validate the module: it
/// returns <see langword="null"/> when it cannot decode it, and the runtime reports the errors of the original module.
/// </para>
/// </remarks>
internal static class LegacyExceptionHandling
{
    /// <summary>Translates the legacy exception handling instructions of a module.</summary>
    /// <param name="module">The bytes of the module.</param>
    /// <returns>The bytes of the translated module, or <see langword="null"/> when the module has no legacy instruction or cannot be decoded.</returns>
    public static byte[]? Translate(byte[] module) => Translate(module, out _);

    /// <summary>Translates the legacy exception handling instructions of a module.</summary>
    /// <param name="module">The bytes of the module.</param>
    /// <param name="decoded">Whether the module could be decoded.</param>
    /// <returns>The bytes of the translated module, or <see langword="null"/> when the module has no legacy instruction or cannot be decoded.</returns>
    public static byte[]? Translate(byte[] module, out bool decoded)
    {
        try
        {
            var result = new ModuleTranslator(module).Translate(out decoded);
            return result;
        }
        catch (InvalidModuleException)
        {
            decoded = false;
            return null;
        }
    }

    private static class Opcodes
    {
        public const byte Block = 0x02;
        public const byte Loop = 0x03;
        public const byte If = 0x04;
        public const byte Else = 0x05;
        public const byte Try = 0x06;
        public const byte Catch = 0x07;
        public const byte Rethrow = 0x09;
        public const byte ThrowRef = 0x0A;
        public const byte End = 0x0B;
        public const byte Br = 0x0C;
        public const byte BrIf = 0x0D;
        public const byte BrTable = 0x0E;
        public const byte Delegate = 0x18;
        public const byte CatchAll = 0x19;
        public const byte TryTable = 0x1F;
        public const byte LocalGet = 0x20;
        public const byte LocalSet = 0x21;
        public const byte BrOnNull = 0xD5;
        public const byte BrOnNonNull = 0xD6;
        public const byte GcPrefix = 0xFB;
        public const byte MiscPrefix = 0xFC;
        public const byte SimdPrefix = 0xFD;
        public const byte AtomicPrefix = 0xFE;
        public const int BrOnCast = (GcPrefix << 16) | 24;
        public const int BrOnCastFail = (GcPrefix << 16) | 25;

        public const byte EmptyBlockType = 0x40;
        public const byte ExnRef = 0x69;
        public const byte FuncType = 0x60;
        public const byte CatchRef = 0x01;
        public const byte CatchAllRef = 0x03;
    }

#pragma warning disable CA1064 // It never leaves the translation: Translate returns null
    private sealed class InvalidModuleException : Exception
#pragma warning restore CA1064
    {
        public InvalidModuleException()
        {
        }

        public InvalidModuleException(string message)
            : base(message)
        {
        }

        public InvalidModuleException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    private sealed record FunctionType(ReadOnlyMemory<byte>[] Parameters, ReadOnlyMemory<byte>[] Results);

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Section(byte Id, int HeaderStart, int Start, int End);

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Instruction(int Start, int End, int Opcode);

    private sealed class TryInfo
    {
        /// <summary>The tags of the catch clauses, -1 for <c>catch_all</c>.</summary>
        public List<int> Catches { get; } = [];

        /// <summary>The index in the control stack of the label of the <c>delegate</c>, -1 for a <c>try</c> without <c>delegate</c>.</summary>
        public int DelegateTarget { get; set; } = -1;
    }

    private enum FrameKind
    {
        Function,
        Block,
        Loop,
        If,
        Try,
        TryTable,
    }

    private sealed class Frame(FrameKind kind, int instructionIndex)
    {
        public FrameKind Kind { get; } = kind;

        /// <summary>The index of the instruction that opens the frame, -1 for the function.</summary>
        public int InstructionIndex { get; } = instructionIndex;

        /// <summary>The part of the code of the frame: 0 for the body (the then branch), 1 for the else branch, n + 1 for the handler of the n-th catch clause.</summary>
        public int Region { get; set; }

        public FunctionType Signature { get; init; } = EmptySignature;

        public TryInfo? Try { get; init; }

        /// <summary>The position of the output block that the branches to the label of the frame target.</summary>
        public int BranchTarget { get; init; }

        /// <summary>The position of the output block to which the <c>delegate</c> instructions that target the current region branch, or -1.</summary>
        public int DelegateBlock { get; set; } = -1;

        /// <summary>The local that holds the exception of the current handler of a <c>try</c>.</summary>
        public uint ExceptionLocal { get; set; }

        public bool IsInHandler => Kind is FrameKind.Try && Region > 0;
    }

    private static readonly FunctionType EmptySignature = new([], []);
    private static readonly ReadOnlyMemory<byte> ExnRefType = new byte[] { Opcodes.ExnRef };

    private sealed class ModuleTranslator(byte[] bytes)
    {
        private const int MaxLocals = 1_000_000;

        private readonly List<FunctionType?> _types = [];
        private readonly List<int> _functionTypes = [];
        private readonly List<int> _tagTypes = [];
        private readonly List<byte[]> _newTypes = [];
        private readonly Dictionary<string, int> _newTypeIndices = new(StringComparer.Ordinal);
        private int _importedFunctionCount;
        private int _typeEntryCount;

        // The state of the function being translated
        private readonly List<Instruction> _instructions = [];
        private readonly Dictionary<int, TryInfo> _tries = [];
        private readonly HashSet<(int InstructionIndex, int Region)> _delegateRegions = [];
        private readonly List<Frame> _frames = [];
        private readonly ArrayBufferWriter<byte> _code = new();
        private int _outputDepth;
        private uint _exceptionLocalBase;
        private int _exceptionLocalCount;

        public byte[]? Translate(out bool decoded)
        {
            decoded = false;
            ReadOnlySpan<byte> header = [0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00];
            if (bytes.Length < header.Length || !bytes.AsSpan(0, header.Length).SequenceEqual(header))
                return null;

            var sections = new List<Section>();
            var reader = new Reader(bytes, header.Length, bytes.Length);
            while (!reader.IsAtEnd)
            {
                var headerStart = reader.Position;
                var id = reader.ReadByte();
                var size = reader.ReadU32();
                var start = reader.Position;
                if (size > (uint)(bytes.Length - start))
                    throw new InvalidModuleException();

                sections.Add(new Section(id, headerStart, start, start + (int)size));
                reader.Position = start + (int)size;
            }

            var codeSection = -1;
            for (var i = 0; i < sections.Count; i++)
            {
                var section = sections[i];
                switch (section.Id)
                {
                    case 1:
                        ReadTypeSection(section);
                        break;
                    case 2:
                        ReadImportSection(section);
                        break;
                    case 3:
                        ReadFunctionSection(section);
                        break;
                    case 10:
                        codeSection = i;
                        break;
                    case 13:
                        ReadTagSection(section);
                        break;
                }
            }

            if (codeSection < 0)
            {
                decoded = true;
                return null;
            }

            var code = TranslateCodeSection(sections[codeSection]);
            decoded = true;
            if (code is null)
                return null;

            var output = new ArrayBufferWriter<byte>(bytes.Length + (bytes.Length / 8));
            output.Write(header);
            var typeSectionWritten = false;
            for (var i = 0; i < sections.Count; i++)
            {
                var section = sections[i];
                if (section.Id is 1)
                {
                    WriteTypeSection(output, section);
                    typeSectionWritten = true;
                }
                else if (i == codeSection)
                {
                    WriteSection(output, 10, code);
                }
                else
                {
                    if (!typeSectionWritten && section.Id is not 0 && _newTypes.Count > 0)
                    {
                        // The module has no type section: it goes before the other sections
                        WriteTypeSection(output, section: null);
                        typeSectionWritten = true;
                    }

                    output.Write(bytes.AsSpan(section.HeaderStart, section.End - section.HeaderStart));
                }
            }

            return output.WrittenSpan.ToArray();
        }

        private void WriteTypeSection(ArrayBufferWriter<byte> output, Section? section)
        {
            if (_newTypes.Count is 0)
            {
                if (section is { } existing)
                {
                    output.Write(bytes.AsSpan(existing.HeaderStart, existing.End - existing.HeaderStart));
                }

                return;
            }

            var content = new ArrayBufferWriter<byte>();
            WriteU32(content, checked((uint)(_typeEntryCount + _newTypes.Count)));
            if (section is { } typeSection)
            {
                var reader = new Reader(bytes, typeSection.Start, typeSection.End);
                reader.ReadU32();
                content.Write(bytes.AsSpan(reader.Position, typeSection.End - reader.Position));
            }

            foreach (var type in _newTypes)
            {
                content.Write(type);
            }

            WriteSection(output, 1, content.WrittenSpan);
        }

        private static void WriteSection(ArrayBufferWriter<byte> output, byte id, ReadOnlySpan<byte> content)
        {
            output.Write([id]);
            WriteU32(output, (uint)content.Length);
            output.Write(content);
        }

        private void ReadTypeSection(Section section)
        {
            var reader = new Reader(bytes, section.Start, section.End);
            var count = reader.ReadU32();
            _typeEntryCount = checked((int)count);
            for (var i = 0u; i < count; i++)
            {
                if (reader.PeekByte() is 0x4E)
                {
                    // A recursion group
                    reader.ReadByte();
                    var subtypes = reader.ReadU32();
                    for (var j = 0u; j < subtypes; j++)
                    {
                        _types.Add(ReadSubType(ref reader));
                    }
                }
                else
                {
                    _types.Add(ReadSubType(ref reader));
                }
            }

            reader.EnsureAtEnd();
        }

        private static FunctionType? ReadSubType(ref Reader reader)
        {
            if (reader.PeekByte() is 0x50 or 0x4F)
            {
                reader.ReadByte();
                var supertypes = reader.ReadU32();
                for (var i = 0u; i < supertypes; i++)
                {
                    reader.ReadU32();
                }
            }

            switch (reader.ReadByte())
            {
                case Opcodes.FuncType:
                    return new FunctionType(ReadValueTypes(ref reader), ReadValueTypes(ref reader));

                case 0x5F: // struct
                    var fields = reader.ReadU32();
                    for (var i = 0u; i < fields; i++)
                    {
                        ReadFieldType(ref reader);
                    }

                    return null;

                case 0x5E: // array
                    ReadFieldType(ref reader);
                    return null;

                default:
                    throw new InvalidModuleException();
            }
        }

        private static void ReadFieldType(ref Reader reader)
        {
            if (reader.PeekByte() is 0x78 or 0x77)
            {
                // i8, i16
                reader.ReadByte();
            }
            else
            {
                reader.SkipValueType();
            }

            reader.ReadByte(); // mutability
        }

        private static ReadOnlyMemory<byte>[] ReadValueTypes(ref Reader reader)
        {
            var count = reader.ReadU32();
            if (count > (uint)reader.Remaining)
                throw new InvalidModuleException();

            var result = new ReadOnlyMemory<byte>[count];
            for (var i = 0; i < result.Length; i++)
            {
                var start = reader.Position;
                reader.SkipValueType();
                result[i] = reader.Bytes.AsMemory(start, reader.Position - start);
            }

            return result;
        }

        private void ReadImportSection(Section section)
        {
            var reader = new Reader(bytes, section.Start, section.End);
            var count = reader.ReadU32();
            for (var i = 0u; i < count; i++)
            {
                reader.SkipName();
                reader.SkipName();
                switch (reader.ReadByte())
                {
                    case 0x00: // function
                        _functionTypes.Add(checked((int)reader.ReadU32()));
                        _importedFunctionCount++;
                        break;

                    case 0x01: // table
                        reader.SkipValueType();
                        reader.SkipLimits();
                        break;

                    case 0x02: // memory
                        reader.SkipLimits();
                        break;

                    case 0x03: // global
                        reader.SkipValueType();
                        reader.ReadByte();
                        break;

                    case 0x04: // tag
                        if (reader.ReadByte() is not 0)
                            throw new InvalidModuleException();

                        _tagTypes.Add(checked((int)reader.ReadU32()));
                        break;

                    default:
                        throw new InvalidModuleException();
                }
            }

            reader.EnsureAtEnd();
        }

        private void ReadFunctionSection(Section section)
        {
            var reader = new Reader(bytes, section.Start, section.End);
            var count = reader.ReadU32();
            for (var i = 0u; i < count; i++)
            {
                _functionTypes.Add(checked((int)reader.ReadU32()));
            }

            reader.EnsureAtEnd();
        }

        private void ReadTagSection(Section section)
        {
            var reader = new Reader(bytes, section.Start, section.End);
            var count = reader.ReadU32();
            for (var i = 0u; i < count; i++)
            {
                if (reader.ReadByte() is not 0)
                    throw new InvalidModuleException();

                _tagTypes.Add(checked((int)reader.ReadU32()));
            }

            reader.EnsureAtEnd();
        }

        private FunctionType GetFunctionType(long typeIndex)
            => (ulong)typeIndex < (ulong)_types.Count && _types[(int)typeIndex] is { } type ? type : throw new InvalidModuleException();

        private FunctionType GetTagType(uint tagIndex)
            => tagIndex < (uint)_tagTypes.Count ? GetFunctionType(_tagTypes[(int)tagIndex]) : throw new InvalidModuleException();

        private byte[]? TranslateCodeSection(Section section)
        {
            var reader = new Reader(bytes, section.Start, section.End);
            var count = reader.ReadU32();
            if (count != (uint)(_functionTypes.Count - _importedFunctionCount))
                throw new InvalidModuleException();

            var output = new ArrayBufferWriter<byte>(section.End - section.Start + 1024);
            WriteU32(output, count);
            var changed = false;
            for (var i = 0; i < count; i++)
            {
                var size = reader.ReadU32();
                var start = reader.Position;
                if (size > (uint)reader.Remaining)
                    throw new InvalidModuleException();

                var end = start + (int)size;
                var body = TranslateFunction(GetFunctionType(_functionTypes[_importedFunctionCount + i]), start, end);
                if (body is null)
                {
                    WriteU32(output, size);
                    output.Write(bytes.AsSpan(start, (int)size));
                }
                else
                {
                    WriteU32(output, (uint)body.Length);
                    output.Write(body);
                    changed = true;
                }

                reader.Position = end;
            }

            reader.EnsureAtEnd();
            return changed ? output.WrittenSpan.ToArray() : null;
        }

        private byte[]? TranslateFunction(FunctionType type, int start, int end)
        {
            var reader = new Reader(bytes, start, end);
            var localGroups = reader.ReadU32();
            var localGroupsStart = reader.Position;
            var localCount = (ulong)type.Parameters.Length;
            for (var i = 0u; i < localGroups; i++)
            {
                localCount += reader.ReadU32();
                if (localCount > MaxLocals)
                    throw new InvalidModuleException();

                reader.SkipValueType();
            }

            var localGroupsEnd = reader.Position;
            if (!DecodeInstructions(ref reader))
                return null;

            Analyze();

            _code.Clear();
            _frames.Clear();
            _exceptionLocalBase = (uint)localCount;
            _exceptionLocalCount = 0;
            Emit(type);

            var output = new ArrayBufferWriter<byte>(end - start + _code.WrittenCount);
            WriteU32(output, localGroups + (_exceptionLocalCount > 0 ? 1u : 0u));
            output.Write(bytes.AsSpan(localGroupsStart, localGroupsEnd - localGroupsStart));
            if (_exceptionLocalCount > 0)
            {
                WriteU32(output, (uint)_exceptionLocalCount);
                output.Write([Opcodes.ExnRef]);
            }

            output.Write(_code.WrittenSpan);
            return output.WrittenSpan.ToArray();
        }

        /// <summary>Decodes the instructions of a function, and returns whether it contains legacy instructions.</summary>
        private bool DecodeInstructions(ref Reader reader)
        {
            _instructions.Clear();
            var hasLegacyInstructions = false;
            var depth = 1;
            while (depth > 0)
            {
                var start = reader.Position;
                var opcode = reader.ReadOpcode();
                reader.SkipImmediates(opcode);
                _instructions.Add(new Instruction(start, reader.Position, opcode));
                switch (opcode)
                {
                    case Opcodes.Block or Opcodes.Loop or Opcodes.If or Opcodes.TryTable:
                        depth++;
                        break;

                    case Opcodes.Try:
                        depth++;
                        hasLegacyInstructions = true;
                        break;

                    case Opcodes.End:
                        depth--;
                        break;

                    case Opcodes.Delegate:
                        depth--;
                        hasLegacyInstructions = true;
                        break;

                    case Opcodes.Catch or Opcodes.CatchAll or Opcodes.Rethrow:
                        hasLegacyInstructions = true;
                        break;
                }
            }

            reader.EnsureAtEnd();
            return hasLegacyInstructions;
        }

        /// <summary>
        /// Finds the catch clauses of the <c>try</c> instructions, the targets of the <c>delegate</c> instructions, and checks
        /// the structure of the legacy instructions.
        /// </summary>
        private void Analyze()
        {
            _tries.Clear();
            _delegateRegions.Clear();
            _frames.Clear();
            _frames.Add(new Frame(FrameKind.Function, -1));
            for (var i = 0; i < _instructions.Count; i++)
            {
                var instruction = _instructions[i];
                switch (instruction.Opcode)
                {
                    case Opcodes.Block:
                        _frames.Add(new Frame(FrameKind.Block, i));
                        break;

                    case Opcodes.Loop:
                        _frames.Add(new Frame(FrameKind.Loop, i));
                        break;

                    case Opcodes.If:
                        _frames.Add(new Frame(FrameKind.If, i));
                        break;

                    case Opcodes.TryTable:
                        _frames.Add(new Frame(FrameKind.TryTable, i));
                        break;

                    case Opcodes.Try:
                        var info = new TryInfo();
                        _tries.Add(i, info);
                        _frames.Add(new Frame(FrameKind.Try, i) { Try = info });
                        break;

                    case Opcodes.Else:
                        if (_frames[^1] is not { Kind: FrameKind.If, Region: 0 } ifFrame)
                            throw new InvalidModuleException();

                        ifFrame.Region = 1;
                        break;

                    case Opcodes.Catch or Opcodes.CatchAll:
                        if (_frames[^1] is not { Kind: FrameKind.Try, Try: { } tryInfo } tryFrame || tryInfo.Catches is [.., -1])
                            throw new InvalidModuleException();

                        tryInfo.Catches.Add(instruction.Opcode is Opcodes.Catch ? ReadTagIndex(instruction) : -1);
                        tryFrame.Region++;
                        break;

                    case Opcodes.Delegate:
                        if (_frames[^1] is not { Kind: FrameKind.Try, Region: 0, Try: { } delegateInfo })
                            throw new InvalidModuleException();

                        _frames.RemoveAt(_frames.Count - 1);
                        var target = GetLabelTarget(ReadLabel(instruction));
                        delegateInfo.DelegateTarget = target;
                        _delegateRegions.Add((_frames[target].InstructionIndex, _frames[target].Region));
                        break;

                    case Opcodes.Rethrow:
                        if (!_frames[GetLabelTarget(ReadLabel(instruction))].IsInHandler)
                            throw new InvalidModuleException();

                        break;

                    case Opcodes.End:
                        _frames.RemoveAt(_frames.Count - 1);
                        break;
                }
            }
        }

        private void Emit(FunctionType type)
        {
            _frames.Add(new Frame(FrameKind.Function, -1) { Signature = new FunctionType([], type.Results), BranchTarget = 0 });
            _outputDepth = 1;
            OpenRegion(_frames[0], []);
            for (var i = 0; i < _instructions.Count; i++)
            {
                var instruction = _instructions[i];
                switch (instruction.Opcode)
                {
                    case Opcodes.Block or Opcodes.Loop or Opcodes.If:
                        {
                            var (signature, _) = ReadBlockType(instruction);
                            CopyInstruction(instruction);
                            var kind = instruction.Opcode switch
                            {
                                Opcodes.Block => FrameKind.Block,
                                Opcodes.Loop => FrameKind.Loop,
                                _ => FrameKind.If,
                            };
                            var frame = PushFrame(kind, i, signature);
                            OpenRegion(frame, signature.Parameters);
                            break;
                        }

                    case Opcodes.TryTable:
                        EmitTryTable(instruction, i);
                        break;

                    case Opcodes.Try:
                        EmitTry(instruction, i);
                        break;

                    case Opcodes.Else:
                        {
                            var frame = _frames[^1];
                            CloseRegion(frame);
                            WriteByte(Opcodes.Else);
                            frame.Region = 1;
                            OpenRegion(frame, frame.Signature.Parameters);
                            break;
                        }

                    case Opcodes.Catch or Opcodes.CatchAll:
                        EmitCatch(instruction);
                        break;

                    case Opcodes.Delegate:
                        CloseRegion(_frames[^1]);
                        WriteByte(Opcodes.End); // try_table
                        PopFrame();
                        break;

                    case Opcodes.Rethrow:
                        {
                            var frame = _frames[GetLabelTarget(ReadLabel(instruction))];
                            WriteByte(Opcodes.LocalGet);
                            WriteU32(_code, frame.ExceptionLocal);
                            WriteByte(Opcodes.ThrowRef);
                            break;
                        }

                    case Opcodes.End:
                        {
                            var frame = _frames[^1];
                            CloseRegion(frame);
                            WriteByte(Opcodes.End);
                            if (frame.Kind is FrameKind.Function)
                            {
                                _frames.Clear();
                            }
                            else
                            {
                                PopFrame();
                            }

                            break;
                        }

                    case Opcodes.Br or Opcodes.BrIf or Opcodes.BrOnNull or Opcodes.BrOnNonNull:
                        WriteByte((byte)instruction.Opcode);
                        WriteU32(_code, GetOutputLabel(ReadLabel(instruction)));
                        break;

                    case Opcodes.BrTable:
                        {
                            var reader = new Reader(bytes, instruction.Start + 1, instruction.End);
                            var count = reader.ReadU32();
                            WriteByte(Opcodes.BrTable);
                            WriteU32(_code, count);
                            for (var j = 0u; j <= count; j++)
                            {
                                WriteU32(_code, GetOutputLabel(reader.ReadU32()));
                            }

                            break;
                        }

                    case Opcodes.BrOnCast or Opcodes.BrOnCastFail:
                        {
                            // 0xFB opcode flags label heaptype heaptype
                            var reader = new Reader(bytes, instruction.Start, instruction.End);
                            reader.ReadOpcode();
                            reader.ReadByte();
                            _code.Write(bytes.AsSpan(instruction.Start, reader.Position - instruction.Start));
                            WriteU32(_code, GetOutputLabel(reader.ReadU32()));
                            _code.Write(bytes.AsSpan(reader.Position, instruction.End - reader.Position));
                            break;
                        }

                    default:
                        CopyInstruction(instruction);
                        break;
                }
            }

            if (_frames.Count is not 0)
                throw new InvalidModuleException();
        }

        private void EmitTryTable(Instruction instruction, int index)
        {
            var (signature, reader) = ReadBlockType(instruction);
            _code.Write(bytes.AsSpan(instruction.Start, reader.Position - instruction.Start));
            var count = reader.ReadU32();
            WriteU32(_code, count);
            for (var i = 0u; i < count; i++)
            {
                var kind = reader.ReadByte();
                WriteByte(kind);
                if (kind is 0x00 or 0x01)
                {
                    WriteU32(_code, reader.ReadU32()); // tag
                }

                // The labels of the catch clauses are outside the try_table
                WriteU32(_code, GetOutputLabel(reader.ReadU32()));
            }

            var frame = PushFrame(FrameKind.TryTable, index, signature);
            OpenRegion(frame, signature.Parameters);
        }

        private void EmitTry(Instruction instruction, int index)
        {
            var (signature, reader) = ReadBlockType(instruction);
            var blockType = bytes.AsSpan(instruction.Start + 1, reader.Position - instruction.Start - 1);
            var info = _tries[index];
            Frame frame;
            if (info.DelegateTarget >= 0)
            {
                // try_table (catch_all_ref $delegate) where $delegate is the block that throws the exception in the target region
                var target = _frames[info.DelegateTarget];
                if (target.DelegateBlock < 0)
                    throw new InvalidModuleException("The target region of the delegate instruction has no delegate block");

                WriteByte(Opcodes.TryTable);
                _code.Write(blockType);
                WriteU32(_code, 1);
                WriteByte(Opcodes.CatchAllRef);
                WriteU32(_code, (uint)(_outputDepth - 1 - target.DelegateBlock));
                frame = PushFrame(FrameKind.Try, index, signature, info);
            }
            else if (info.Catches.Count is 0)
            {
                // A try without handler is a block
                WriteByte(Opcodes.Block);
                _code.Write(blockType);
                frame = PushFrame(FrameKind.Try, index, signature, info);
            }
            else
            {
                WriteByte(Opcodes.Block);
                _code.Write(blockType);
                frame = PushFrame(FrameKind.Try, index, signature, info);

                // One block per catch clause, the first clause is the innermost one
                var catchBlocks = new int[info.Catches.Count];
                for (var i = info.Catches.Count - 1; i >= 0; i--)
                {
                    var results = info.Catches[i] is var tag and >= 0 ? [.. GetTagType((uint)tag).Parameters, ExnRefType] : new[] { ExnRefType };
                    WriteByte(Opcodes.Block);
                    WriteBlockType(signature.Parameters, results);
                    catchBlocks[i] = _outputDepth;
                    _outputDepth++;
                }

                WriteByte(Opcodes.TryTable);
                _code.Write(blockType);
                WriteU32(_code, (uint)info.Catches.Count);
                for (var i = 0; i < info.Catches.Count; i++)
                {
                    if (info.Catches[i] is var tag and >= 0)
                    {
                        WriteByte(Opcodes.CatchRef);
                        WriteU32(_code, (uint)tag);
                    }
                    else
                    {
                        WriteByte(Opcodes.CatchAllRef);
                    }

                    WriteU32(_code, (uint)(_outputDepth - 1 - catchBlocks[i]));
                }

                _outputDepth++;
            }

            OpenRegion(frame, signature.Parameters);
        }

        private void EmitCatch(Instruction instruction)
        {
            var frame = _frames[^1];
            CloseRegion(frame);
            if (frame.Region is 0)
            {
                // The end of the try_table
                WriteByte(Opcodes.End);
                _outputDepth--;
            }

            // The end of the body or of the previous handler, and of the block of the catch clause
            WriteByte(Opcodes.Br);
            WriteU32(_code, (uint)(_outputDepth - 1 - frame.BranchTarget));
            WriteByte(Opcodes.End);
            _outputDepth--;

            var handlers = 0;
            foreach (var other in _frames)
            {
                if (other != frame && other.IsInHandler)
                {
                    handlers++;
                }
            }

            frame.Region++;
            frame.ExceptionLocal = _exceptionLocalBase + (uint)handlers;
            _exceptionLocalCount = Math.Max(_exceptionLocalCount, handlers + 1);
            WriteByte(Opcodes.LocalSet);
            WriteU32(_code, frame.ExceptionLocal);
            OpenRegion(frame, instruction.Opcode is Opcodes.Catch ? GetTagType((uint)ReadTagIndex(instruction)).Parameters : []);
        }

        private Frame PushFrame(FrameKind kind, int instructionIndex, FunctionType signature, TryInfo? info = null)
        {
            var frame = new Frame(kind, instructionIndex) { Signature = signature, Try = info, BranchTarget = _outputDepth };
            _frames.Add(frame);
            _outputDepth++;
            return frame;
        }

        private void PopFrame()
        {
            _frames.RemoveAt(_frames.Count - 1);
            _outputDepth--;
        }

        /// <summary>Opens the blocks of the region of a frame that the <c>delegate</c> instructions target.</summary>
        private void OpenRegion(Frame frame, ReadOnlyMemory<byte>[] parameters)
        {
            if (!_delegateRegions.Contains((frame.InstructionIndex, frame.Region)))
                return;

            // block $after (param P) (result T)
            //   block $delegate (param P) (result exnref)
            //     code of the region
            //     br $after
            //   end
            //   throw_ref
            // end
            WriteByte(Opcodes.Block);
            WriteBlockType(parameters, frame.Signature.Results);
            _outputDepth++;
            WriteByte(Opcodes.Block);
            WriteBlockType(parameters, [ExnRefType]);
            frame.DelegateBlock = _outputDepth;
            _outputDepth++;
        }

        private void CloseRegion(Frame frame)
        {
            if (frame.DelegateBlock < 0)
                return;

            WriteByte(Opcodes.Br);
            WriteU32(_code, 1);
            WriteByte(Opcodes.End);
            WriteByte(Opcodes.ThrowRef);
            WriteByte(Opcodes.End);
            _outputDepth -= 2;
            frame.DelegateBlock = -1;
        }

        private int GetLabelTarget(uint label)
            => label < (uint)_frames.Count ? _frames.Count - 1 - (int)label : throw new InvalidModuleException();

        private uint GetOutputLabel(uint label) => (uint)(_outputDepth - 1 - _frames[GetLabelTarget(label)].BranchTarget);

        private uint ReadLabel(Instruction instruction)
        {
            var reader = new Reader(bytes, instruction.Start + 1, instruction.End);
            return reader.ReadU32();
        }

        private int ReadTagIndex(Instruction instruction)
        {
            var reader = new Reader(bytes, instruction.Start + 1, instruction.End);
            var tag = reader.ReadU32();
            GetTagType(tag);
            return (int)tag;
        }

        /// <summary>Reads the block type of a block, loop, if, try or try_table instruction.</summary>
        private (FunctionType Signature, Reader Reader) ReadBlockType(Instruction instruction)
        {
            var reader = new Reader(bytes, instruction.Start + 1, instruction.End);
            var start = reader.Position;
            var first = reader.PeekByte();
            if (first is Opcodes.EmptyBlockType)
            {
                reader.ReadByte();
                return (EmptySignature, reader);
            }

            if (Reader.IsValueTypeStart(first))
            {
                reader.SkipValueType();
                return (new FunctionType([], [bytes.AsMemory(start, reader.Position - start)]), reader);
            }

            var typeIndex = reader.ReadS33();
            return (GetFunctionType(typeIndex), reader);
        }

        private void WriteBlockType(ReadOnlyMemory<byte>[] parameters, ReadOnlyMemory<byte>[] results)
        {
            if (parameters.Length is 0 && results.Length is 0)
            {
                WriteByte(Opcodes.EmptyBlockType);
                return;
            }

            if (parameters.Length is 0 && results.Length is 1)
            {
                _code.Write(results[0].Span);
                return;
            }

            var type = new ArrayBufferWriter<byte>();
            type.Write([Opcodes.FuncType]);
            WriteU32(type, (uint)parameters.Length);
            foreach (var parameter in parameters)
            {
                type.Write(parameter.Span);
            }

            WriteU32(type, (uint)results.Length);
            foreach (var result in results)
            {
                type.Write(result.Span);
            }

            var key = Convert.ToHexString(type.WrittenSpan);
            if (!_newTypeIndices.TryGetValue(key, out var index))
            {
                index = checked(_types.Count + _newTypes.Count);
                _newTypes.Add(type.WrittenSpan.ToArray());
                _newTypeIndices.Add(key, index);
            }

            WriteS33(_code, index);
        }

        private void CopyInstruction(Instruction instruction) => _code.Write(bytes.AsSpan(instruction.Start, instruction.End - instruction.Start));

        private void WriteByte(byte value) => _code.Write([value]);
    }

    private static void WriteU32(ArrayBufferWriter<byte> output, uint value)
    {
        Span<byte> buffer = stackalloc byte[5];
        var length = 0;
        do
        {
            var b = (byte)(value & 0x7F);
            value >>= 7;
            if (value is not 0)
            {
                b |= 0x80;
            }

            buffer[length++] = b;
        }
        while (value is not 0);

        output.Write(buffer[..length]);
    }

    private static void WriteS33(ArrayBufferWriter<byte> output, long value)
    {
        Span<byte> buffer = stackalloc byte[5];
        var length = 0;
        while (true)
        {
            var b = (byte)(value & 0x7F);
            value >>= 7;
            var done = (value is 0 && (b & 0x40) is 0) || (value is -1 && (b & 0x40) is not 0);
            buffer[length++] = done ? b : (byte)(b | 0x80);
            if (done)
                break;
        }

        output.Write(buffer[..length]);
    }

    /// <summary>Reads the binary format of WebAssembly (https://webassembly.github.io/spec/core/binary/index.html).</summary>
    private struct Reader(byte[] bytes, int position, int end)
    {
        public readonly byte[] Bytes => bytes;

        public int Position { readonly get; set; } = position;

        public readonly bool IsAtEnd => Position >= end;

        public readonly int Remaining => end - Position;

        public readonly void EnsureAtEnd()
        {
            if (Position != end)
                throw new InvalidModuleException();
        }

        public readonly byte PeekByte() => Position < end ? bytes[Position] : throw new InvalidModuleException();

        public byte ReadByte()
        {
            var value = PeekByte();
            Position++;
            return value;
        }

        public void Skip(int count)
        {
            if (count > Remaining)
                throw new InvalidModuleException();

            Position += count;
        }

        public uint ReadU32()
        {
            var value = ReadUnsigned(maxBytes: 5);
            return value <= uint.MaxValue ? (uint)value : throw new InvalidModuleException();
        }

        public ulong ReadU64() => ReadUnsigned(maxBytes: 10);

        private ulong ReadUnsigned(int maxBytes)
        {
            ulong result = 0;
            for (var i = 0; i < maxBytes; i++)
            {
                var b = ReadByte();
                result |= (ulong)(b & 0x7F) << (7 * i);
                if ((b & 0x80) is 0)
                    return result;
            }

            throw new InvalidModuleException();
        }

        public long ReadS33() => ReadSigned(maxBytes: 5);

        public long ReadSigned(int maxBytes)
        {
            long result = 0;
            for (var i = 0; i < maxBytes; i++)
            {
                var b = ReadByte();
                result |= (long)(b & 0x7F) << (7 * i);
                if ((b & 0x80) is 0)
                {
                    var shift = 7 * (i + 1);
                    if (shift < 64 && (b & 0x40) is not 0)
                    {
                        result |= -1L << shift;
                    }

                    return result;
                }
            }

            throw new InvalidModuleException();
        }

        public void SkipName() => Skip(checked((int)ReadU32()));

        public void SkipLimits()
        {
            var flags = ReadByte();
            if ((flags & ~0x0F) is not 0)
                throw new InvalidModuleException();

            ReadU64();
            if ((flags & 0x01) is not 0)
            {
                ReadU64();
            }

            if ((flags & 0x08) is not 0)
            {
                ReadU32(); // the log2 of the page size
            }
        }

        public static bool IsValueTypeStart(byte value) => value is >= 0x63 and <= 0x75 or >= 0x7B and <= 0x7F;

        public void SkipValueType()
        {
            switch (ReadByte())
            {
                case 0x63 or 0x64: // (ref null ht), (ref ht)
                    ReadS33();
                    break;

                case >= 0x68 and <= 0x75 or >= 0x7B and <= 0x7F:
                    break;

                default:
                    throw new InvalidModuleException();
            }
        }

        private void SkipBlockType()
        {
            var first = PeekByte();
            if (first is Opcodes.EmptyBlockType)
            {
                ReadByte();
            }
            else if (IsValueTypeStart(first))
            {
                SkipValueType();
            }
            else if (ReadS33() < 0)
            {
                throw new InvalidModuleException();
            }
        }

        private void SkipMemoryArgument()
        {
            var flags = ReadU32();
            if ((flags & 0x40) is not 0)
            {
                ReadU32(); // memory index
            }

            ReadU64(); // offset
        }

        /// <summary>Reads an opcode: a byte, or a prefix and a sub-opcode encoded as <c>prefix &lt;&lt; 16 | sub-opcode</c>.</summary>
        public int ReadOpcode()
        {
            var opcode = ReadByte();
            if (opcode is Opcodes.GcPrefix or Opcodes.MiscPrefix or Opcodes.SimdPrefix or Opcodes.AtomicPrefix)
            {
                var subOpcode = ReadU32();
                if (subOpcode > 0xFFFF)
                    throw new InvalidModuleException();

                return (opcode << 16) | (int)subOpcode;
            }

            return opcode;
        }

        public void SkipImmediates(int opcode)
        {
            if (opcode > 0xFF)
            {
                var subOpcode = opcode & 0xFFFF;
                switch ((byte)(opcode >> 16))
                {
                    case Opcodes.GcPrefix:
                        SkipGcImmediates(subOpcode);
                        break;

                    case Opcodes.MiscPrefix:
                        SkipMiscImmediates(subOpcode);
                        break;

                    case Opcodes.SimdPrefix:
                        SkipSimdImmediates(subOpcode);
                        break;

                    default:
                        SkipAtomicImmediates(subOpcode);
                        break;
                }

                return;
            }

            switch (opcode)
            {
                case 0x00 or 0x01 or 0x05 or 0x0A or 0x0B or 0x0F or 0x19 or 0x1A or 0x1B:
                case >= 0x45 and <= 0xC4:
                case 0xD1 or 0xD3 or 0xD4:
                    break;

                case 0x02 or 0x03 or 0x04 or 0x06:
                    SkipBlockType();
                    break;

                case 0x07 or 0x08 or 0x09 or 0x0C or 0x0D or 0x10 or 0x12 or 0x14 or 0x15 or 0x18:
                case >= 0x20 and <= 0x26:
                case 0x3F or 0x40 or 0xD2 or 0xD5 or 0xD6:
                    ReadU32();
                    break;

                case 0x0E:
                    var labels = ReadU32();
                    for (var i = 0u; i <= labels; i++)
                    {
                        ReadU32();
                    }

                    break;

                case 0x11 or 0x13: // call_indirect, return_call_indirect
                    ReadU32();
                    ReadU32();
                    break;

                case 0x1C: // select t*
                    var types = ReadU32();
                    for (var i = 0u; i < types; i++)
                    {
                        SkipValueType();
                    }

                    break;

                case 0x1F: // try_table
                    SkipBlockType();
                    var catches = ReadU32();
                    for (var i = 0u; i < catches; i++)
                    {
                        var kind = ReadByte();
                        if (kind is 0x00 or 0x01)
                        {
                            ReadU32();
                        }
                        else if (kind is not (0x02 or 0x03))
                        {
                            throw new InvalidModuleException();
                        }

                        ReadU32();
                    }

                    break;

                case >= 0x28 and <= 0x3E:
                    SkipMemoryArgument();
                    break;

                case 0x41:
                    ReadSigned(maxBytes: 5);
                    break;

                case 0x42:
                    ReadSigned(maxBytes: 10);
                    break;

                case 0x43:
                    Skip(4);
                    break;

                case 0x44:
                    Skip(8);
                    break;

                case 0xD0: // ref.null ht
                    ReadS33();
                    break;

                default:
                    throw new InvalidModuleException($"Unknown opcode 0x{opcode:X2}");
            }
        }

        private void SkipAtomicImmediates(int opcode)
        {
            if (opcode is 0x03)
            {
                ReadByte(); // atomic.fence
            }
            else if (opcode is <= 0x02 or >= 0x10 and <= 0x4E)
            {
                SkipMemoryArgument();
            }
            else
            {
                throw new InvalidModuleException();
            }
        }

        private void SkipGcImmediates(int opcode)
        {
            switch (opcode)
            {
                case 0 or 1 or 6 or 7 or 11 or 12 or 13 or 14 or 16:
                    ReadU32();
                    break;

                case 2 or 3 or 4 or 5 or 8 or 9 or 10 or 17 or 18 or 19:
                    ReadU32();
                    ReadU32();
                    break;

                case 15 or >= 26 and <= 30:
                    break;

                case >= 20 and <= 23: // ref.test, ref.cast
                    ReadS33();
                    break;

                case 24 or 25: // br_on_cast, br_on_cast_fail
                    ReadByte();
                    ReadU32();
                    ReadS33();
                    ReadS33();
                    break;

                default:
                    throw new InvalidModuleException();
            }
        }

        private void SkipMiscImmediates(int opcode)
        {
            switch (opcode)
            {
                case <= 7 or >= 19 and <= 22:
                    break;

                case 9 or 11 or 13 or 15 or 16 or 17:
                    ReadU32();
                    break;

                case 8 or 10 or 12 or 14:
                    ReadU32();
                    ReadU32();
                    break;

                default:
                    throw new InvalidModuleException();
            }
        }

        private void SkipSimdImmediates(int opcode)
        {
            switch (opcode)
            {
                case <= 11 or 92 or 93:
                    SkipMemoryArgument();
                    break;

                case 12 or 13: // v128.const, i8x16.shuffle
                    Skip(16);
                    break;

                case >= 21 and <= 34: // extract_lane, replace_lane
                    ReadByte();
                    break;

                case >= 84 and <= 91: // load_lane, store_lane
                    SkipMemoryArgument();
                    ReadByte();
                    break;

                case <= 0x113:
                    break;

                default:
                    throw new InvalidModuleException();
            }
        }
    }
}
