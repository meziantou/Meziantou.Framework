using System.Runtime.InteropServices;

namespace Meziantou.Framework.Fonts;

/// <summary>
/// Runs TrueType instructions: the font program (<c>fpgm</c>), the control value program (<c>prep</c>) and the
/// instructions of a glyph, which move the points of the glyph to the pixel grid of a size.
/// </summary>
/// <remarks>
/// <para>
/// Written from the OpenType specification ("TrueType Instructions", "Instructing TrueType Glyphs") and the Apple
/// TrueType Reference Manual (chapters 2 to 6). FreeType is a test oracle only.
/// </para>
/// <para>
/// Numbers are those of the specification: distances and coordinates are 26.6 fixed-point pixels, vectors are 2.14
/// fixed-point unit vectors. Everything the font gives is bounded: the stack, the storage area, the control values,
/// the functions and the twilight points are sized from the <c>maxp</c> table (at most 65,535 entries each), the
/// calls are nested at most <see cref="MaxCallDepth"/> deep, and every instruction, every repetition of an
/// instruction and every point an instruction moves is charged to the <see cref="GlyphBudget"/>. A program that
/// breaks a rule of the specification (an invalid opcode, a point that does not exist, a stack that underflows)
/// stops with a <see cref="HintingException"/>: the glyph is then drawn without hinting.
/// </para>
/// </remarks>
internal sealed partial class TrueTypeInterpreter : IDisposable
{
    /// <summary>The greatest nesting of function calls.</summary>
    public const int MaxCallDepth = 64;

    /// <summary>What a font may push beyond the size of the stack it declares: fonts commonly declare a little less than they use.</summary>
    public const int StackSlack = 32;

    // The version of the interpreter GETINFO reports, and how the glyphs are rendered: in black and white.
    private const int InterpreterVersion = 40;

    private readonly byte[] _code;
    private readonly FontAllocator _allocator;
    private readonly CallFrame[] _callStack = new CallFrame[MaxCallDepth];
    private readonly CodeRange[] _instructionDefinitions = new CodeRange[256];
    private readonly int _axisCount;

    private FontBuffer? _stackBuffer;
    private readonly int _stackCapacity;
    private int _stackCount;

    private FontBuffer? _storageBuffer;
    private readonly int _storageCount;

    private FontBuffer? _cvtBuffer;
    private readonly int _cvtCount;

    private FontBuffer? _functionBuffer;
    private readonly int _functionCount;

    private FontBuffer? _twilightBuffer;
    private readonly int _twilightCount;

    private FontBuffer? _glyphBuffer;
    private int _glyphCapacity;
    private int _glyphPointCount;
    private FontBuffer? _contourBuffer;
    private int _contourCapacity;
    private int _contourCount;

    private GraphicsState _state;
    private int _freedomDotProjection;

    // The program being run: the instruction pointer and the range it may move in, absolute in the bytes of the font.
    private int _ip;
    private int _rangeStart;
    private int _rangeEnd;
    private int _callDepth;
    private bool _isGlyphProgram;

    /// <summary>Initializes an interpreter for a font: its storage is sized from the <c>maxp</c> table.</summary>
    /// <param name="code">The bytes of the font; programs are ranges of it.</param>
    /// <param name="allocator">The storage of the stack, the storage area, the control values, the functions and the zones.</param>
    /// <param name="maxp">The <c>maxp</c> table, in its version 1.0.</param>
    /// <param name="cvtCount">The number of control values.</param>
    /// <param name="axisCount">The number of axes of a variable font, which GETINFO and GETVARIATION report; 0 for another font.</param>
    public TrueTypeInterpreter(byte[] code, FontAllocator allocator, ReadOnlySpan<byte> maxp, int cvtCount, int axisCount)
    {
        _code = code;
        _allocator = allocator;
        _axisCount = axisCount;
        _twilightCount = FontBinary.UInt16(maxp, 16);
        _storageCount = FontBinary.UInt16(maxp, 18);
        _functionCount = FontBinary.UInt16(maxp, 20);
        _stackCapacity = FontBinary.UInt16(maxp, 24) + StackSlack;
        _cvtCount = cvtCount;
        try
        {
            _stackBuffer = allocator.Rent<int>(_stackCapacity);
            _storageBuffer = allocator.Rent<int>(_storageCount);
            _cvtBuffer = allocator.Rent<int>(_cvtCount);
            _functionBuffer = allocator.Rent<CodeRange>(_functionCount);
            _twilightBuffer = allocator.Rent<HintPoint>(_twilightCount);
        }
        catch
        {
            Dispose();
            throw;
        }

        _state = GraphicsState.Default;
        UpdateVectors();
    }

    /// <summary>
    /// Gets or sets a value indicating whether the points of the glyph zone have positions in font units: those of a
    /// simple glyph have; the points of a composite glyph only have where its components put them.
    /// </summary>
    public bool HasUnscaledPositions { get; set; }

    /// <summary>Gets or sets the number of pixels per em that MPPEM reports.</summary>
    public int PixelsPerEm { get; set; }

    /// <summary>Gets or sets the scale from font units to 26.6 pixels, as a 16.16 fixed-point number.</summary>
    public int Scale { get; set; }

    /// <summary>Gets the control values, in 26.6 pixels.</summary>
    public Span<int> ControlValues => unsafe(MemoryMarshal.Cast<byte, int>(_cvtBuffer!.Span))[.._cvtCount];

    /// <summary>Gets the storage area.</summary>
    public Span<int> Storage => unsafe(MemoryMarshal.Cast<byte, int>(_storageBuffer!.Span))[.._storageCount];

    /// <summary>Gets the points of the twilight zone.</summary>
    public Span<HintPoint> TwilightPoints => unsafe(MemoryMarshal.Cast<byte, HintPoint>(_twilightBuffer!.Span))[.._twilightCount];

    /// <summary>Gets the points of the glyph zone: the points of the glyph, then its phantom points.</summary>
    public Span<HintPoint> GlyphPoints => _glyphBuffer is null ? default : unsafe(MemoryMarshal.Cast<byte, HintPoint>(_glyphBuffer.Span))[.._glyphPointCount];

    /// <summary>Gets the index of the last point of each contour of the glyph zone.</summary>
    public Span<int> ContourEnds => _contourBuffer is null ? default : unsafe(MemoryMarshal.Cast<byte, int>(_contourBuffer.Span))[.._contourCount];

    /// <summary>Gets the graphics state, which the control value program sets for the glyphs.</summary>
    public GraphicsState State => _state;

    private Span<int> Stack => unsafe(MemoryMarshal.Cast<byte, int>(_stackBuffer!.Span))[.._stackCapacity];

    private Span<CodeRange> Functions => unsafe(MemoryMarshal.Cast<byte, CodeRange>(_functionBuffer!.Span))[.._functionCount];

    /// <summary>Makes room for the points and the contours of a glyph zone, whose content is unspecified.</summary>
    public void PrepareGlyphZone(int pointCount, int contourCount)
    {
        if (pointCount > _glyphCapacity)
        {
            var capacity = Math.Max(pointCount, _glyphCapacity * 2);
            var buffer = _allocator.Rent<HintPoint>(capacity);
            _glyphBuffer?.Dispose();
            _glyphBuffer = buffer;
            _glyphCapacity = capacity;
        }

        if (contourCount > _contourCapacity)
        {
            var capacity = Math.Max(contourCount, _contourCapacity * 2);
            var buffer = _allocator.Rent<int>(capacity);
            _contourBuffer?.Dispose();
            _contourBuffer = buffer;
            _contourCapacity = capacity;
        }

        _glyphPointCount = pointCount;
        _contourCount = contourCount;
    }

    /// <summary>Runs the font program, which defines the functions.</summary>
    public void RunFontProgram(int start, int length, ref GlyphBudget budget) => Run(start, length, isGlyphProgram: false, ref budget);

    /// <summary>Runs the control value program with the default graphics state: the state it leaves is what the glyph programs start with.</summary>
    public void RunControlValueProgram(int start, int length, ref GlyphBudget budget)
    {
        _state = GraphicsState.Default;
        UpdateVectors();
        Run(start, length, isGlyphProgram: false, ref budget);
    }

    /// <summary>Runs the instructions of a glyph with a graphics state.</summary>
    public void RunGlyphProgram(int start, int length, in GraphicsState state, ref GlyphBudget budget)
    {
        _state = state;
        _state.ResetForGlyph();
        UpdateVectors();
        Run(start, length, isGlyphProgram: true, ref budget);
    }

    /// <summary>Gives back the storage.</summary>
    public void Dispose()
    {
        _stackBuffer?.Dispose();
        _stackBuffer = null;
        _storageBuffer?.Dispose();
        _storageBuffer = null;
        _cvtBuffer?.Dispose();
        _cvtBuffer = null;
        _functionBuffer?.Dispose();
        _functionBuffer = null;
        _twilightBuffer?.Dispose();
        _twilightBuffer = null;
        _glyphBuffer?.Dispose();
        _glyphBuffer = null;
        _contourBuffer?.Dispose();
        _contourBuffer = null;
    }

    private void Run(int start, int length, bool isGlyphProgram, ref GlyphBudget budget)
    {
        _isGlyphProgram = isGlyphProgram;
        _stackCount = 0;
        _callDepth = 0;
        _ip = start;
        _rangeStart = start;
        _rangeEnd = start + length;
        while (true)
        {
            if (_ip >= _rangeEnd)
            {
                // A program ends at the end of its bytes; a function ends at its ENDF.
                if (_callDepth != 0)
                    throw Error("A function has no ENDF.");

                return;
            }

            budget.Charge(1);
            Execute(_code[_ip], ref budget);
        }
    }

    /// <summary>Runs one instruction and moves the instruction pointer after it, or where it jumps.</summary>
    private void Execute(int opcode, ref GlyphBudget budget)
    {
        switch (opcode)
        {
            case 0x00 or 0x01:
                SetVectorsToAxis(opcode, projection: true, freedom: true);
                break;
            case 0x02 or 0x03:
                SetVectorsToAxis(opcode, projection: true, freedom: false);
                break;
            case 0x04 or 0x05:
                SetVectorsToAxis(opcode, projection: false, freedom: true);
                break;
            case 0x06 or 0x07:
                SetVectorToLine(opcode, isProjection: true, isDual: false);
                break;
            case 0x08 or 0x09:
                SetVectorToLine(opcode, isProjection: false, isDual: false);
                break;
            case 0x0A:
                SetVectorFromStack(isProjection: true);
                break;
            case 0x0B:
                SetVectorFromStack(isProjection: false);
                break;
            case 0x0C:
                Push(_state.ProjectionX);
                Push(_state.ProjectionY);
                break;
            case 0x0D:
                Push(_state.FreedomX);
                Push(_state.FreedomY);
                break;
            case 0x0E:
                _state.FreedomX = _state.ProjectionX;
                _state.FreedomY = _state.ProjectionY;
                UpdateVectors();
                break;
            case 0x0F:
                Intersect();
                break;
            case 0x10:
                _state.Rp0 = Pop();
                break;
            case 0x11:
                _state.Rp1 = Pop();
                break;
            case 0x12:
                _state.Rp2 = Pop();
                break;
            case >= 0x13 and <= 0x16:
                SetZonePointer(opcode);
                break;
            case 0x17:
                SetLoop();
                break;
            case 0x18:
                _state.RoundMode = RoundMode.Grid;
                break;
            case 0x19:
                _state.RoundMode = RoundMode.HalfGrid;
                break;
            case 0x1A:
                _state.MinimumDistance = Pop();
                break;
            case 0x1B:
                // ELSE reached by running the IF part: skip to the EIF.
                SkipConditional(stopAtElse: false, ref budget);
                return;
            case 0x1C:
                Jump(Pop());
                return;
            case 0x1D:
                _state.ControlValueCutIn = Pop();
                break;
            case 0x1E:
                _state.SingleWidthCutIn = Pop();
                break;
            case 0x1F:
                _state.SingleWidthValue = ScaleFontUnits(Pop());
                break;
            case 0x20:
                Duplicate();
                break;
            case 0x21:
                Pop();
                break;
            case 0x22:
                _stackCount = 0;
                break;
            case 0x23:
                Swap();
                break;
            case 0x24:
                Push(_stackCount);
                break;
            case 0x25:
                CopyIndexed();
                break;
            case 0x26:
                MoveIndexed();
                break;
            case 0x27:
                AlignPoints();
                break;
            case 0x29:
                UntouchPoint();
                break;
            case 0x2A:
                LoopCall();
                return;
            case 0x2B:
                Call();
                return;
            case 0x2C:
                DefineFunction(ref budget);
                return;
            case 0x2D:
                EndFunction();
                return;
            case 0x2E or 0x2F:
                MoveDirectAbsolutePoint((opcode & 1) != 0);
                break;
            case 0x30 or 0x31:
                InterpolateUntouchedPoints(isX: (opcode & 1) != 0, ref budget);
                break;
            case 0x32 or 0x33:
                ShiftPoints(opcode, ref budget);
                break;
            case 0x34 or 0x35:
                ShiftContour(opcode, ref budget);
                break;
            case 0x36 or 0x37:
                ShiftZone(opcode, ref budget);
                break;
            case 0x38:
                ShiftPointsByPixels(ref budget);
                break;
            case 0x39:
                InterpolatePoints(ref budget);
                break;
            case 0x3A or 0x3B:
                MoveStackIndirectRelativePoint((opcode & 1) != 0);
                break;
            case 0x3C:
                AlignToReferencePoint(ref budget);
                break;
            case 0x3D:
                _state.RoundMode = RoundMode.DoubleGrid;
                break;
            case 0x3E or 0x3F:
                MoveIndirectAbsolutePoint((opcode & 1) != 0);
                break;
            case 0x40:
                PushBytes(_code[CheckedIndex(_ip + 1)], 2);
                return;
            case 0x41:
                PushWords(_code[CheckedIndex(_ip + 1)], 2);
                return;
            case 0x42:
                WriteStorage();
                break;
            case 0x43:
                ReadStorage();
                break;
            case 0x44:
                WriteControlValueInPixels();
                break;
            case 0x45:
                ReadControlValue();
                break;
            case 0x46 or 0x47:
                GetCoordinate((opcode & 1) != 0);
                break;
            case 0x48:
                SetCoordinateFromStack();
                break;
            case 0x49 or 0x4A:
                MeasureDistance((opcode & 1) != 0);
                break;
            case 0x4B:
                Push(PixelsPerEm);
                break;
            case 0x4C:
                Push(PixelsPerEm * 64);
                break;
            case 0x4D:
                _state.AutoFlip = true;
                break;
            case 0x4E:
                _state.AutoFlip = false;
                break;
            case 0x4F:
                Pop();
                break;
            case >= 0x50 and <= 0x55:
                Compare(opcode);
                break;
            case 0x56:
                Push((Round(Pop()) & 127) == 64 ? 1 : 0);
                break;
            case 0x57:
                Push((Round(Pop()) & 127) == 0 ? 1 : 0);
                break;
            case 0x58:
                if (Pop() == 0)
                {
                    SkipConditional(stopAtElse: true, ref budget);
                    return;
                }

                break;
            case 0x59:
                break;
            case 0x5A:
            {
                var b = Pop();
                var a = Pop();
                Push(a != 0 && b != 0 ? 1 : 0);
                break;
            }

            case 0x5B:
            {
                var b = Pop();
                var a = Pop();
                Push(a != 0 || b != 0 ? 1 : 0);
                break;
            }

            case 0x5C:
                Push(Pop() == 0 ? 1 : 0);
                break;
            case 0x5D:
                DeltaPoints(0, ref budget);
                break;
            case 0x5E:
                _state.DeltaBase = Pop();
                break;
            case 0x5F:
                _state.DeltaShift = Pop();
                if ((uint)_state.DeltaShift > 6)
                    throw Error("DELTA shift out of range.");

                break;
            case >= 0x60 and <= 0x67:
                Arithmetic(opcode);
                break;
            case >= 0x68 and <= 0x6B:
                Push(Round(Pop()));
                break;
            case >= 0x6C and <= 0x6F:
                // NROUND: no engine compensation in this interpreter.
                Push(Pop());
                break;
            case 0x70:
                WriteControlValueInFontUnits();
                break;
            case 0x71:
                DeltaPoints(16, ref budget);
                break;
            case 0x72:
                DeltaPoints(32, ref budget);
                break;
            case >= 0x73 and <= 0x75:
                DeltaControlValues((opcode - 0x73) * 16, ref budget);
                break;
            case 0x76:
                SetSuperRound(Pop(), RoundMode.Super);
                break;
            case 0x77:
                SetSuperRound(Pop(), RoundMode.Super45);
                break;
            case 0x78:
            case 0x79:
            {
                var condition = Pop();
                var offset = Pop();
                if ((condition != 0) == (opcode == 0x78))
                {
                    Jump(offset);
                    return;
                }

                break;
            }

            case 0x7A:
                _state.RoundMode = RoundMode.Off;
                break;
            case 0x7C:
                _state.RoundMode = RoundMode.UpToGrid;
                break;
            case 0x7D:
                _state.RoundMode = RoundMode.DownToGrid;
                break;
            case 0x7E:
            case 0x7F:
                // SANGW and AA are obsolete: their argument is ignored.
                Pop();
                break;
            case 0x80:
                FlipPoints(ref budget);
                break;
            case 0x81:
                FlipRange(onCurve: true, ref budget);
                break;
            case 0x82:
                FlipRange(onCurve: false, ref budget);
                break;
            case 0x85:
                // SCANCTRL: dropout control is a rule of rasterizers of pixels without coverage.
                Pop();
                break;
            case 0x86 or 0x87:
                SetVectorToLine(opcode, isProjection: true, isDual: true);
                break;
            case 0x88:
                GetInformation();
                break;
            case 0x89:
                DefineInstruction(ref budget);
                return;
            case 0x8A:
                Roll();
                break;
            case 0x8B:
            {
                var b = Pop();
                var a = Pop();
                Push(Math.Max(a, b));
                break;
            }

            case 0x8C:
            {
                var b = Pop();
                var a = Pop();
                Push(Math.Min(a, b));
                break;
            }

            case 0x8D:
                // SCANTYPE: see SCANCTRL.
                Pop();
                break;
            case 0x8E:
                SetInstructionControl();
                break;
            case 0x91 when _axisCount > 0:
                // GETVARIATION: the hinted instances are the default instance, whose coordinates are zero.
                for (var i = 0; i < _axisCount; i++)
                {
                    Push(0);
                }

                break;
            case 0x92:
                // GETDATA
                Push(17);
                break;
            case >= 0xB0 and <= 0xB7:
                PushBytes(opcode - 0xAF, 1);
                return;
            case >= 0xB8 and <= 0xBF:
                PushWords(opcode - 0xB7, 1);
                return;
            case >= 0xC0 and <= 0xDF:
                MoveDirectRelativePoint(opcode);
                break;
            case >= 0xE0:
                MoveIndirectRelativePoint(opcode);
                break;
            default:
                CallInstructionDefinition(opcode);
                return;
        }

        _ip++;
    }

    private static HintingException Error(string message) => new(message);

    private int CheckedIndex(int index)
    {
        if (index >= _rangeEnd)
            throw Error("An instruction is truncated.");

        return index;
    }

    // ---- Stack -------------------------------------------------------------------------------------------------

    private void Push(int value)
    {
        if (_stackCount >= _stackCapacity)
            throw Error("The stack overflows.");

        Stack[_stackCount++] = value;
    }

    private int Pop()
    {
        if (_stackCount == 0)
            throw Error("The stack underflows.");

        return Stack[--_stackCount];
    }

    private void PushBytes(int count, int headerLength)
    {
        var start = _ip + headerLength;
        if (start + count > _rangeEnd)
            throw Error("A push instruction is truncated.");

        for (var i = 0; i < count; i++)
        {
            Push(_code[start + i]);
        }

        _ip = start + count;
    }

    private void PushWords(int count, int headerLength)
    {
        var start = _ip + headerLength;
        if (start + (count * 2) > _rangeEnd)
            throw Error("A push instruction is truncated.");

        for (var i = 0; i < count; i++)
        {
            Push((short)((_code[start + (i * 2)] << 8) | _code[start + (i * 2) + 1]));
        }

        _ip = start + (count * 2);
    }

    private void Duplicate()
    {
        var value = Pop();
        Push(value);
        Push(value);
    }

    private void Swap()
    {
        var b = Pop();
        var a = Pop();
        Push(b);
        Push(a);
    }

    private void CopyIndexed()
    {
        var index = Pop();
        if (index <= 0 || index > _stackCount)
            throw Error("CINDEX refers to an element the stack does not have.");

        Push(Stack[_stackCount - index]);
    }

    private void MoveIndexed()
    {
        var index = Pop();
        if (index <= 0 || index > _stackCount)
            throw Error("MINDEX refers to an element the stack does not have.");

        var stack = Stack;
        var position = _stackCount - index;
        var value = stack[position];
        stack.Slice(position + 1, index - 1).CopyTo(stack[position..]);
        stack[_stackCount - 1] = value;
    }

    private void Roll()
    {
        var a = Pop();
        var b = Pop();
        var c = Pop();
        Push(b);
        Push(a);
        Push(c);
    }

    // ---- Control flow ------------------------------------------------------------------------------------------

    /// <summary>Gets the length of the instruction at a position: its opcode and the data a push carries.</summary>
    private int GetInstructionLength(int position)
    {
        var opcode = _code[position];
        var length = opcode switch
        {
            0x40 => 2 + _code[CheckedIndex(position + 1)],
            0x41 => 2 + (2 * _code[CheckedIndex(position + 1)]),
            >= 0xB0 and <= 0xB7 => 1 + (opcode - 0xAF),
            >= 0xB8 and <= 0xBF => 1 + (2 * (opcode - 0xB7)),
            _ => 1,
        };

        if (position + length > _rangeEnd)
            throw Error("An instruction is truncated.");

        return length;
    }

    /// <summary>Skips a part of an IF that is not run: to its ELSE (after it) or its EIF.</summary>
    private void SkipConditional(bool stopAtElse, ref GlyphBudget budget)
    {
        var depth = 1;
        var position = _ip + GetInstructionLength(_ip);
        while (position < _rangeEnd)
        {
            budget.Charge(1);
            var opcode = _code[position];
            switch (opcode)
            {
                case 0x58:
                    depth++;
                    break;
                case 0x1B when depth == 1 && stopAtElse:
                    _ip = position + 1;
                    return;
                case 0x59:
                    depth--;
                    if (depth == 0)
                    {
                        _ip = position + 1;
                        return;
                    }

                    break;
            }

            position += GetInstructionLength(position);
        }

        throw Error("An IF has no EIF.");
    }

    private void Jump(int offset)
    {
        var target = (long)_ip + offset;
        if (target < _rangeStart || target > _rangeEnd)
            throw Error("A jump leaves its program.");

        _ip = (int)target;
    }

    private void DefineFunction(ref GlyphBudget budget)
    {
        if (_isGlyphProgram)
            throw Error("A glyph program defines a function.");

        var number = Pop();
        if ((uint)number >= (uint)_functionCount)
            throw Error("A function number is greater than the maxp table allows.");

        var end = FindEndOfDefinition(ref budget);
        Functions[number] = new CodeRange(_ip + 1, end + 1);
        _ip = end + 1;
    }

    private void DefineInstruction(ref GlyphBudget budget)
    {
        if (_isGlyphProgram)
            throw Error("A glyph program defines an instruction.");

        var opcode = Pop();
        if ((uint)opcode > 255)
            throw Error("IDEF defines an opcode that does not exist.");

        var end = FindEndOfDefinition(ref budget);
        _instructionDefinitions[opcode] = new CodeRange(_ip + 1, end + 1);
        _ip = end + 1;
    }

    /// <summary>Finds the ENDF of the definition that starts at the instruction pointer.</summary>
    private int FindEndOfDefinition(ref GlyphBudget budget)
    {
        var position = _ip + 1;
        while (position < _rangeEnd)
        {
            budget.Charge(1);
            var opcode = _code[position];
            if (opcode is 0x2C or 0x89)
                throw Error("A definition is nested in another one.");

            if (opcode == 0x2D)
                return position;

            position += GetInstructionLength(position);
        }

        throw Error("A definition has no ENDF.");
    }

    private void Call() => Enter(Pop(), 1);

    private void LoopCall()
    {
        var number = Pop();
        var count = Pop();
        if (count <= 0)
        {
            if ((uint)number >= (uint)_functionCount || !Functions[number].IsDefined)
                throw Error("LOOPCALL calls a function that is not defined.");

            _ip++;
            return;
        }

        Enter(number, count);
    }

    private void Enter(int number, int count)
    {
        if ((uint)number >= (uint)_functionCount || !Functions[number].IsDefined)
            throw Error("CALL calls a function that is not defined.");

        EnterRange(Functions[number], count);
    }

    private void EnterRange(CodeRange range, int count)
    {
        if (_callDepth >= MaxCallDepth)
            throw Error("The calls are nested too deeply.");

        _callStack[_callDepth++] = new CallFrame(_ip + 1, _rangeStart, _rangeEnd, range, count);
        _ip = range.Start;
        _rangeStart = range.Start;
        _rangeEnd = range.End;
    }

    private void EndFunction()
    {
        if (_callDepth == 0)
            throw Error("ENDF outside a function.");

        ref var frame = ref _callStack[_callDepth - 1];
        if (--frame.Count > 0)
        {
            _ip = frame.Range.Start;
            return;
        }

        _ip = frame.ReturnPosition;
        _rangeStart = frame.ReturnStart;
        _rangeEnd = frame.ReturnEnd;
        _callDepth--;
    }

    private void CallInstructionDefinition(int opcode)
    {
        var definition = _instructionDefinitions[opcode];
        if (!definition.IsDefined)
            throw Error($"The opcode 0x{opcode:X2} is not an instruction.");

        EnterRange(definition, 1);
    }

    // ---- Graphics state ----------------------------------------------------------------------------------------

    private void UpdateVectors()
    {
        var dot = (int)((((long)_state.FreedomX * _state.ProjectionX) + ((long)_state.FreedomY * _state.ProjectionY)) >> 14);

        // A freedom vector almost perpendicular to the projection vector would move points without bound.
        _freedomDotProjection = Math.Abs(dot) < 0x400 ? 0x4000 : dot;
    }

    private void SetVectorsToAxis(int opcode, bool projection, bool freedom)
    {
        var isX = (opcode & 1) != 0;
        var x = isX ? 0x4000 : 0;
        var y = isX ? 0 : 0x4000;
        if (projection)
        {
            _state.ProjectionX = _state.DualX = x;
            _state.ProjectionY = _state.DualY = y;
        }

        if (freedom)
        {
            _state.FreedomX = x;
            _state.FreedomY = y;
        }

        UpdateVectors();
    }

    private void SetVectorToLine(int opcode, bool isProjection, bool isDual)
    {
        // The line from p1, in the zone zp2, to p2, in the zone zp1; p1 is at the top of the stack.
        var p1 = Pop();
        var p2 = Pop();
        ref var first = ref GetPoint(_state.Zp2, p1);
        ref var second = ref GetPoint(_state.Zp1, p2);
        var isPerpendicular = (opcode & 1) != 0;
        Normalize(second.X - first.X, second.Y - first.Y, isPerpendicular, out var x, out var y);
        if (isDual)
        {
            // The dual projection vector is the same line in the original outline.
            Normalize(second.OriginalX - first.OriginalX, second.OriginalY - first.OriginalY, isPerpendicular, out var dualX, out var dualY);
            _state.ProjectionX = x;
            _state.ProjectionY = y;
            _state.DualX = dualX;
            _state.DualY = dualY;
        }
        else if (isProjection)
        {
            _state.ProjectionX = _state.DualX = x;
            _state.ProjectionY = _state.DualY = y;
        }
        else
        {
            _state.FreedomX = x;
            _state.FreedomY = y;
        }

        UpdateVectors();
    }

    private void SetVectorFromStack(bool isProjection)
    {
        var y = Pop();
        var x = Pop();
        Normalize(x, y, isPerpendicular: false, out var unitX, out var unitY);
        if (isProjection)
        {
            _state.ProjectionX = _state.DualX = unitX;
            _state.ProjectionY = _state.DualY = unitY;
        }
        else
        {
            _state.FreedomX = unitX;
            _state.FreedomY = unitY;
        }

        UpdateVectors();
    }

    /// <summary>Gets the 2.14 unit vector of a direction, turned a quarter of a turn counterclockwise when it is perpendicular.</summary>
    private static void Normalize(int dx, int dy, bool isPerpendicular, out int x, out int y)
    {
        if (isPerpendicular)
        {
            (dx, dy) = (-dy, dx);
        }

        if (dx == 0 && dy == 0)
        {
            x = 0x4000;
            y = 0;
            return;
        }

        // Truncated toward zero: a unit vector is never longer than one.
        var length = Math.Sqrt(((double)dx * dx) + ((double)dy * dy));
        x = (int)(dx / length * 0x4000);
        y = (int)(dy / length * 0x4000);
    }

    private void SetZonePointer(int opcode)
    {
        var zone = Pop();
        if ((uint)zone > 1)
            throw Error("A zone pointer is neither 0 nor 1.");

        switch (opcode)
        {
            case 0x13:
                _state.Zp0 = zone;
                break;
            case 0x14:
                _state.Zp1 = zone;
                break;
            case 0x15:
                _state.Zp2 = zone;
                break;
            default:
                _state.Zp0 = _state.Zp1 = _state.Zp2 = zone;
                break;
        }
    }

    private void SetLoop()
    {
        var count = Pop();
        if (count < 0)
            throw Error("SLOOP sets a negative count.");

        _state.Loop = count;
    }

    private void SetSuperRound(int value, RoundMode mode)
    {
        // The period, the phase and the threshold, in 26.6 pixels; the 45-degree period is the grid along a diagonal.
        var gridPeriod = mode == RoundMode.Super45 ? 45 : 64;
        var period = (value & 0xC0) switch
        {
            0x00 => gridPeriod / 2,
            0x40 => gridPeriod,
            0x80 => gridPeriod * 2,
            _ => gridPeriod,
        };

        _state.RoundPeriod = period;
        _state.RoundPhase = ((value & 0x30) >> 4) * period / 4;
        _state.RoundThreshold = (value & 0x0F) == 0 ? period - 1 : ((value & 0x0F) - 4) * period / 8;
        _state.RoundMode = RoundMode.Super;
    }

    private void SetInstructionControl()
    {
        var selector = Pop();
        var value = Pop();
        if (selector is < 1 or > 3)
            throw Error("INSTCTRL has a selector that does not exist.");

        // Only the control value program sets it.
        if (_isGlyphProgram)
            return;

        var bit = 1 << (selector - 1);
        _state.InstructControl = (_state.InstructControl & ~bit) | (value & bit);
    }

    private void GetInformation()
    {
        var selector = Pop();
        var result = 0;
        if ((selector & 1) != 0)
        {
            result |= InterpreterVersion;
        }

        // Not rotated, not stretched; a variable font says so. The glyphs are hinted for black and white pixels:
        // no grayscale, no ClearType.
        if ((selector & 8) != 0 && _axisCount > 0)
        {
            result |= 1 << 10;
        }

        Push(result);
    }

    // ---- Storage and control values ----------------------------------------------------------------------------

    private void WriteStorage()
    {
        var value = Pop();
        var index = Pop();
        if ((uint)index >= (uint)_storageCount)
            throw Error("WS writes a location the maxp table does not declare.");

        Storage[index] = value;
    }

    private void ReadStorage()
    {
        var index = Pop();
        if ((uint)index >= (uint)_storageCount)
            throw Error("RS reads a location the maxp table does not declare.");

        Push(Storage[index]);
    }

    private void WriteControlValueInPixels()
    {
        var value = Pop();
        var index = Pop();
        if ((uint)index >= (uint)_cvtCount)
            throw Error("WCVTP writes a control value that does not exist.");

        ControlValues[index] = value;
    }

    private void WriteControlValueInFontUnits()
    {
        var value = Pop();
        var index = Pop();
        if ((uint)index >= (uint)_cvtCount)
            throw Error("WCVTF writes a control value that does not exist.");

        ControlValues[index] = ScaleFontUnits(value);
    }

    private void ReadControlValue()
    {
        var index = Pop();
        if ((uint)index >= (uint)_cvtCount)
            throw Error("RCVT reads a control value that does not exist.");

        Push(ControlValues[index]);
    }

    private int GetControlValue(int index)
    {
        if ((uint)index >= (uint)_cvtCount)
            throw Error("An instruction reads a control value that does not exist.");

        return ControlValues[index];
    }

    /// <summary>Scales font units to 26.6 pixels.</summary>
    public int ScaleFontUnits(int value) => MultiplyFixed(value, Scale);

    // ---- Arithmetic --------------------------------------------------------------------------------------------

    private void Compare(int opcode)
    {
        var b = Pop();
        var a = Pop();
        var result = opcode switch
        {
            0x50 => a < b,
            0x51 => a <= b,
            0x52 => a > b,
            0x53 => a >= b,
            0x54 => a == b,
            _ => a != b,
        };

        Push(result ? 1 : 0);
    }

    private void Arithmetic(int opcode)
    {
        switch (opcode)
        {
            case 0x60:
            {
                var b = Pop();
                var a = Pop();
                Push(unchecked(a + b));
                break;
            }

            case 0x61:
            {
                var b = Pop();
                var a = Pop();
                Push(unchecked(a - b));
                break;
            }

            case 0x62:
            {
                var b = Pop();
                var a = Pop();
                if (b == 0)
                    throw Error("DIV divides by zero.");

                Push(Saturate((long)a * 64 / b));
                break;
            }

            case 0x63:
            {
                var b = Pop();
                var a = Pop();
                Push(MultiplyDivide(a, b, 64));
                break;
            }

            case 0x64:
                Push(Math.Abs(Pop()));
                break;
            case 0x65:
                Push(unchecked(-Pop()));
                break;
            case 0x66:
                Push(Pop() & -64);
                break;
            default:
                Push(unchecked(Pop() + 63) & -64);
                break;
        }
    }

    private static int Saturate(long value) => (int)Math.Clamp(value, int.MinValue, int.MaxValue);

    /// <summary>Gets <c>a * b / c</c>, rounded to the nearest integer, halves away from zero.</summary>
    internal static int MultiplyDivide(long a, long b, long c)
    {
        if (c == 0)
            return a * b >= 0 ? int.MaxValue : int.MinValue;

        var negative = (a < 0) ^ (b < 0) ^ (c < 0);
        var product = Math.Abs(a) * Math.Abs(b);
        var divisor = Math.Abs(c);
        var result = (product + (divisor / 2)) / divisor;
        return Saturate(negative ? -result : result);
    }

    /// <summary>Multiplies by a 16.16 fixed-point number, rounded to the nearest integer, halves away from zero.</summary>
    internal static int MultiplyFixed(long a, long b) => MultiplyDivide(a, b, 0x10000);

    /// <summary>Multiplies by a 2.14 fixed-point number, rounded to the nearest integer.</summary>
    private static int MultiplyFix14(long a, int b) => Saturate(((a * b) + 0x2000) >> 14);

    // ---- Rounding ----------------------------------------------------------------------------------------------

    /// <summary>Rounds a distance with the round state.</summary>
    private int Round(int distance)
    {
        long d = distance;
        long result;
        switch (_state.RoundMode)
        {
            case RoundMode.Grid:
                result = d >= 0 ? (d + 32) & -64 : -((-d + 32) & -64);
                break;
            case RoundMode.HalfGrid:
                result = d >= 0 ? (d & -64) + 32 : -((-d & -64) + 32);
                break;
            case RoundMode.DoubleGrid:
                result = d >= 0 ? (d + 16) & -32 : -((-d + 16) & -32);
                break;
            case RoundMode.DownToGrid:
                result = d >= 0 ? d & -64 : -(-d & -64);
                break;
            case RoundMode.UpToGrid:
                result = d >= 0 ? (d + 63) & -64 : -((-d + 63) & -64);
                break;
            case RoundMode.Super:
            case RoundMode.Super45:
                result = SuperRound(d);
                break;
            default:
                result = d;
                break;
        }

        return Saturate(result);
    }

    private long SuperRound(long d)
    {
        var period = _state.RoundPeriod;
        var phase = _state.RoundPhase;
        var threshold = _state.RoundThreshold;
        if (d >= 0)
        {
            var value = FloorTo(d - phase + threshold, period) + phase;
            return value < 0 ? phase : value;
        }

        var negative = -(FloorTo(-d - phase + threshold, period) + phase);
        return negative > 0 ? -phase : negative;

        static long FloorTo(long value, long period) => (value >= 0 ? value / period : -((-value + period - 1) / period)) * period;
    }

    // ---- Points ------------------------------------------------------------------------------------------------

    private Span<HintPoint> GetZone(int zone) => zone == 0 ? TwilightPoints : GlyphPoints;

    private ref HintPoint GetPoint(int zone, int index)
    {
        var points = GetZone(zone);
        if ((uint)index >= (uint)points.Length)
            throw Error("An instruction refers to a point that does not exist.");

        return ref points[index];
    }

    private int Project(int dx, int dy) => DotFix14(dx, dy, _state.ProjectionX, _state.ProjectionY);

    private int DualProject(int dx, int dy) => DotFix14(dx, dy, _state.DualX, _state.DualY);

    private static int DotFix14(int ax, int ay, int bx, int by) => Saturate((((long)ax * bx) + ((long)ay * by) + 0x2000) >> 14);

    /// <summary>Gets the distance between two points along the projection vector, where they are.</summary>
    private int ProjectCurrent(in HintPoint a, in HintPoint b) => Project(a.X - b.X, a.Y - b.Y);

    /// <summary>
    /// Gets the distance between two points along the dual projection vector, where they were before the
    /// instructions: from their positions in the font, scaled, when both are points of the glyph, which is more
    /// precise than the difference of two rounded positions.
    /// </summary>
    private int ProjectOriginal(int zoneA, in HintPoint a, int zoneB, in HintPoint b)
    {
        if (zoneA == 1 && zoneB == 1 && HasUnscaledPositions)
            return MultiplyFixed(DualProject(a.UnscaledX - b.UnscaledX, a.UnscaledY - b.UnscaledY), Scale);

        return DualProject(a.OriginalX - b.OriginalX, a.OriginalY - b.OriginalY);
    }

    /// <summary>Moves a point along the freedom vector so that its projection changes by a distance.</summary>
    private void Move(ref HintPoint point, int distance, bool touch = true)
    {
        if (_state.FreedomX != 0)
        {
            point.X = Saturate(point.X + (long)MultiplyDivide(distance, _state.FreedomX, _freedomDotProjection));
            if (touch)
            {
                point.Flags |= HintPoint.TouchedX;
            }
        }

        if (_state.FreedomY != 0)
        {
            point.Y = Saturate(point.Y + (long)MultiplyDivide(distance, _state.FreedomY, _freedomDotProjection));
            if (touch)
            {
                point.Flags |= HintPoint.TouchedY;
            }
        }
    }

    private int TakeLoop()
    {
        var count = _state.Loop;
        _state.Loop = 1;
        return count;
    }

    private void MoveDirectAbsolutePoint(bool round)
    {
        var index = Pop();
        ref var point = ref GetPoint(_state.Zp0, index);
        var distance = 0;
        if (round)
        {
            var current = Project(point.X, point.Y);
            distance = Round(current) - current;
        }

        Move(ref point, distance);
        _state.Rp0 = _state.Rp1 = index;
    }

    private void MoveIndirectAbsolutePoint(bool round)
    {
        var cvt = Pop();
        var index = Pop();
        ref var point = ref GetPoint(_state.Zp0, index);
        var distance = GetControlValue(cvt);
        if (_state.Zp0 == 0)
        {
            // A point of the twilight zone is created where the control value says, along the projection vector.
            point.OriginalX = MultiplyFix14(distance, _state.ProjectionX);
            point.OriginalY = MultiplyFix14(distance, _state.ProjectionY);
            point.X = point.OriginalX;
            point.Y = point.OriginalY;
        }

        var current = Project(point.X, point.Y);
        if (round)
        {
            if (Math.Abs((long)distance - current) > _state.ControlValueCutIn)
            {
                distance = current;
            }

            distance = Round(distance);
        }

        Move(ref point, distance - current);
        _state.Rp0 = _state.Rp1 = index;
    }

    private void MoveDirectRelativePoint(int opcode)
    {
        var index = Pop();
        ref var reference = ref GetPoint(_state.Zp0, _state.Rp0);
        ref var point = ref GetPoint(_state.Zp1, index);
        var original = ProjectOriginal(_state.Zp1, point, _state.Zp0, reference);
        if (_state.SingleWidthCutIn > 0 && Math.Abs((long)original - _state.SingleWidthValue) < _state.SingleWidthCutIn)
        {
            original = original >= 0 ? _state.SingleWidthValue : -_state.SingleWidthValue;
        }

        var distance = (opcode & 4) != 0 ? Round(original) : original;
        if ((opcode & 8) != 0)
        {
            distance = ApplyMinimumDistance(original, distance);
        }

        Move(ref point, distance - ProjectCurrent(point, reference));
        _state.Rp1 = _state.Rp0;
        _state.Rp2 = index;
        if ((opcode & 0x10) != 0)
        {
            _state.Rp0 = index;
        }
    }

    private void MoveIndirectRelativePoint(int opcode)
    {
        var cvt = Pop();
        var index = Pop();
        var cvtDistance = GetControlValue(cvt);
        ref var reference = ref GetPoint(_state.Zp0, _state.Rp0);
        ref var point = ref GetPoint(_state.Zp1, index);
        if (_state.SingleWidthCutIn > 0 && Math.Abs((long)cvtDistance - _state.SingleWidthValue) < _state.SingleWidthCutIn)
        {
            cvtDistance = cvtDistance >= 0 ? _state.SingleWidthValue : -_state.SingleWidthValue;
        }

        if (_state.Zp1 == 0)
        {
            // A point of the twilight zone is created at the control value from the reference point.
            point.OriginalX = Saturate(reference.OriginalX + (long)MultiplyFix14(cvtDistance, _state.FreedomX));
            point.OriginalY = Saturate(reference.OriginalY + (long)MultiplyFix14(cvtDistance, _state.FreedomY));
            point.X = point.OriginalX;
            point.Y = point.OriginalY;
        }

        var original = ProjectOriginal(_state.Zp1, point, _state.Zp0, reference);
        var current = ProjectCurrent(point, reference);
        if (_state.AutoFlip && (original ^ cvtDistance) < 0)
        {
            cvtDistance = -cvtDistance;
        }

        int distance;
        if ((opcode & 4) != 0)
        {
            // The control value is only used when it is close enough to the distance in the outline.
            if (_state.Zp0 == _state.Zp1 && Math.Abs((long)cvtDistance - original) > _state.ControlValueCutIn)
            {
                cvtDistance = original;
            }

            distance = Round(cvtDistance);
        }
        else
        {
            distance = cvtDistance;
        }

        if ((opcode & 8) != 0)
        {
            distance = ApplyMinimumDistance(original, distance);
        }

        Move(ref point, distance - current);
        _state.Rp1 = _state.Rp0;
        _state.Rp2 = index;
        if ((opcode & 0x10) != 0)
        {
            _state.Rp0 = index;
        }
    }

    private int ApplyMinimumDistance(int original, int distance)
    {
        var minimum = _state.MinimumDistance;
        if (original >= 0)
            return distance < minimum ? minimum : distance;

        return distance > -minimum ? -minimum : distance;
    }

    private void MoveStackIndirectRelativePoint(bool setRp0)
    {
        var distance = Pop();
        var index = Pop();
        ref var reference = ref GetPoint(_state.Zp0, _state.Rp0);
        ref var point = ref GetPoint(_state.Zp1, index);
        if (_state.Zp1 == 0)
        {
            point.OriginalX = reference.OriginalX;
            point.OriginalY = reference.OriginalY;
            point.X = point.OriginalX;
            point.Y = point.OriginalY;
        }

        Move(ref point, distance - ProjectCurrent(point, reference));
        _state.Rp1 = _state.Rp0;
        _state.Rp2 = index;
        if (setRp0)
        {
            _state.Rp0 = index;
        }
    }

    private void AlignToReferencePoint(ref GlyphBudget budget)
    {
        var count = TakeLoop();
        for (var i = 0; i < count; i++)
        {
            budget.Charge(1);
            var index = Pop();
            ref var reference = ref GetPoint(_state.Zp0, _state.Rp0);
            ref var point = ref GetPoint(_state.Zp1, index);
            Move(ref point, -ProjectCurrent(point, reference));
        }
    }

    private void AlignPoints()
    {
        var p2 = Pop();
        var p1 = Pop();
        ref var first = ref GetPoint(_state.Zp1, p1);
        ref var second = ref GetPoint(_state.Zp0, p2);
        var distance = ProjectCurrent(second, first) / 2;
        Move(ref first, distance);
        Move(ref second, -distance);
    }

    private void UntouchPoint()
    {
        ref var point = ref GetPoint(_state.Zp0, Pop());
        if (_state.FreedomX != 0)
        {
            point.Flags &= ~HintPoint.TouchedX;
        }

        if (_state.FreedomY != 0)
        {
            point.Flags &= ~HintPoint.TouchedY;
        }
    }

    private void Intersect()
    {
        var b1 = Pop();
        var b0 = Pop();
        var a1 = Pop();
        var a0 = Pop();
        var index = Pop();
        ref var point = ref GetPoint(_state.Zp2, index);
        var pa0 = GetPoint(_state.Zp1, a0);
        var pa1 = GetPoint(_state.Zp1, a1);
        var pb0 = GetPoint(_state.Zp0, b0);
        var pb1 = GetPoint(_state.Zp0, b1);

        long dax = pa1.X - pa0.X;
        long day = pa1.Y - pa0.Y;
        long dbx = pb1.X - pb0.X;
        long dby = pb1.Y - pb0.Y;
        long dx = pb0.X - pa0.X;
        long dy = pb0.Y - pa0.Y;
        var discriminant = (dax * -dby) + (day * dbx);
        var dotProduct = (dax * dbx) + (day * dby);

        // Lines too close to parallel meet at the middle of the four points.
        if (Math.Abs(discriminant) * 19 > Math.Abs(dotProduct))
        {
            var value = (dx * -dby) + (dy * dbx);
            point.X = Saturate(pa0.X + (long)MultiplyDivide(dax, value, discriminant));
            point.Y = Saturate(pa0.Y + (long)MultiplyDivide(day, value, discriminant));
        }
        else
        {
            point.X = (int)((pa0.X + (long)pa1.X + pb0.X + pb1.X) / 4);
            point.Y = (int)((pa0.Y + (long)pa1.Y + pb0.Y + pb1.Y) / 4);
        }

        point.Flags |= HintPoint.TouchedX | HintPoint.TouchedY;
    }

    /// <summary>Gets how far the reference point of SHP, SHC and SHZ moved along the projection vector.</summary>
    private int GetReferenceShift(int opcode, out int zone, out int index)
    {
        if ((opcode & 1) != 0)
        {
            zone = _state.Zp0;
            index = _state.Rp1;
        }
        else
        {
            zone = _state.Zp1;
            index = _state.Rp2;
        }

        ref var reference = ref GetPoint(zone, index);
        return Project(reference.X - reference.OriginalX, reference.Y - reference.OriginalY);
    }

    private void ShiftPoints(int opcode, ref GlyphBudget budget)
    {
        var shift = GetReferenceShift(opcode, out _, out _);
        var count = TakeLoop();
        for (var i = 0; i < count; i++)
        {
            budget.Charge(1);
            Move(ref GetPoint(_state.Zp2, Pop()), shift);
        }
    }

    private void ShiftContour(int opcode, ref GlyphBudget budget)
    {
        var contour = Pop();
        var shift = GetReferenceShift(opcode, out var referenceZone, out var referenceIndex);
        if (_state.Zp2 != 1 || (uint)contour >= (uint)_contourCount)
            throw Error("SHC refers to a contour that does not exist.");

        var contours = ContourEnds;
        var start = contour == 0 ? 0 : contours[contour - 1] + 1;
        var end = contours[contour];
        var points = GlyphPoints;
        for (var i = start; i <= end; i++)
        {
            budget.Charge(1);
            if (referenceZone == 1 && i == referenceIndex)
                continue;

            Move(ref points[i], shift);
        }
    }

    private void ShiftZone(int opcode, ref GlyphBudget budget)
    {
        var zone = Pop();
        if ((uint)zone > 1)
            throw Error("SHZ refers to a zone that does not exist.");

        var shift = GetReferenceShift(opcode, out var referenceZone, out var referenceIndex);
        var points = GetZone(zone);

        // The phantom points are not points of the outline.
        var count = zone == 1 ? Math.Max(0, points.Length - TrueTypeHinter.PhantomPointCount) : points.Length;
        for (var i = 0; i < count; i++)
        {
            budget.Charge(1);
            if (referenceZone == zone && i == referenceIndex)
                continue;

            Move(ref points[i], shift, touch: false);
        }
    }

    private void ShiftPointsByPixels(ref GlyphBudget budget)
    {
        var distance = Pop();
        var dx = MultiplyFix14(distance, _state.FreedomX);
        var dy = MultiplyFix14(distance, _state.FreedomY);
        var count = TakeLoop();
        for (var i = 0; i < count; i++)
        {
            budget.Charge(1);
            ref var point = ref GetPoint(_state.Zp2, Pop());
            point.X = Saturate(point.X + (long)dx);
            point.Y = Saturate(point.Y + (long)dy);
            if (_state.FreedomX != 0)
            {
                point.Flags |= HintPoint.TouchedX;
            }

            if (_state.FreedomY != 0)
            {
                point.Flags |= HintPoint.TouchedY;
            }
        }
    }

    private void InterpolatePoints(ref GlyphBudget budget)
    {
        var count = TakeLoop();
        ref var first = ref GetPoint(_state.Zp0, _state.Rp1);
        ref var second = ref GetPoint(_state.Zp1, _state.Rp2);
        // The ratio of the original distances is taken in font units when every point is a point of a simple glyph.
        var unscaled = HasUnscaledPositions && _state.Zp0 == 1 && _state.Zp1 == 1 && _state.Zp2 == 1;
        var originalRange = unscaled ? DualProject(second.UnscaledX - first.UnscaledX, second.UnscaledY - first.UnscaledY) : ProjectOriginal(_state.Zp1, second, _state.Zp0, first);
        var currentRange = ProjectCurrent(second, first);
        for (var i = 0; i < count; i++)
        {
            budget.Charge(1);
            ref var point = ref GetPoint(_state.Zp2, Pop());
            var original = unscaled ? DualProject(point.UnscaledX - first.UnscaledX, point.UnscaledY - first.UnscaledY) : ProjectOriginal(_state.Zp2, point, _state.Zp0, first);
            var current = ProjectCurrent(point, first);
            var target = originalRange != 0 ? MultiplyDivide(original, currentRange, originalRange) : unscaled ? MultiplyFixed(original, Scale) : original;
            Move(ref point, target - current);
        }
    }

    private void InterpolateUntouchedPoints(bool isX, ref GlyphBudget budget)
    {
        if (_state.Zp2 != 1)
            return;

        var points = GlyphPoints;
        var touched = isX ? HintPoint.TouchedX : HintPoint.TouchedY;
        var contours = ContourEnds;
        var start = 0;
        foreach (var end in contours)
        {
            budget.Charge(end - start + 1);
            var contour = points.Slice(start, end - start + 1);
            start = end + 1;

            var firstTouched = -1;
            for (var i = 0; i < contour.Length; i++)
            {
                if ((contour[i].Flags & touched) != 0)
                {
                    firstTouched = i;
                    break;
                }
            }

            if (firstTouched < 0)
                continue;

            // Each run of untouched points between two touched points (around the contour) is interpolated.
            var previous = firstTouched;
            for (var step = 1; step <= contour.Length; step++)
            {
                var index = (firstTouched + step) % contour.Length;
                if ((contour[index].Flags & touched) == 0)
                    continue;

                InterpolateRun(contour, previous, index, isX);
                previous = index;
            }
        }
    }

    /// <summary>Interpolates the points strictly between two touched points of a contour, going forward around it.</summary>
    private static void InterpolateRun(Span<HintPoint> contour, int from, int to, bool isX)
    {
        var count = to > from ? to - from - 1 : contour.Length - from + to - 1;
        if (count <= 0)
            return;

        ref var a = ref contour[from];
        ref var b = ref contour[to];
        int originalA, originalB, currentA, currentB, unscaledA, unscaledB;
        if (isX)
        {
            (originalA, originalB, currentA, currentB, unscaledA, unscaledB) = (a.OriginalX, b.OriginalX, a.X, b.X, a.UnscaledX, b.UnscaledX);
        }
        else
        {
            (originalA, originalB, currentA, currentB, unscaledA, unscaledB) = (a.OriginalY, b.OriginalY, a.Y, b.Y, a.UnscaledY, b.UnscaledY);
        }

        if (originalA > originalB)
        {
            (originalA, originalB) = (originalB, originalA);
            (currentA, currentB) = (currentB, currentA);
            (unscaledA, unscaledB) = (unscaledB, unscaledA);
        }

        var shiftA = currentA - originalA;
        var shiftB = currentB - originalB;

        // The points between are placed with the ratio of the two distances, as a 16.16 fixed-point number.
        var ratio = unscaledA != unscaledB ? MultiplyDivide(currentB - currentA, 0x10000, unscaledB - unscaledA) : 0;
        for (var k = 1; k <= count; k++)
        {
            ref var point = ref contour[(from + k) % contour.Length];
            var original = isX ? point.OriginalX : point.OriginalY;
            var unscaled = isX ? point.UnscaledX : point.UnscaledY;
            int value;
            if (original <= originalA)
            {
                value = original + shiftA;
            }
            else if (original >= originalB)
            {
                value = original + shiftB;
            }
            else
            {
                value = unscaledA != unscaledB
                    ? currentA + MultiplyFixed(unscaled - unscaledA, ratio)
                    : currentA + MultiplyDivide(original - originalA, currentB - currentA, originalB - originalA);
            }

            if (isX)
            {
                point.X = value;
            }
            else
            {
                point.Y = value;
            }
        }
    }

    private void GetCoordinate(bool original)
    {
        ref var point = ref GetPoint(_state.Zp2, Pop());
        Push(original ? DualProject(point.OriginalX, point.OriginalY) : Project(point.X, point.Y));
    }

    private void SetCoordinateFromStack()
    {
        var value = Pop();
        var index = Pop();
        ref var point = ref GetPoint(_state.Zp2, index);
        Move(ref point, value - Project(point.X, point.Y));
        if (_state.Zp2 == 0)
        {
            point.OriginalX = point.X;
            point.OriginalY = point.Y;
        }
    }

    private void MeasureDistance(bool current)
    {
        var p2 = Pop();
        var p1 = Pop();
        ref var first = ref GetPoint(_state.Zp0, p1);
        ref var second = ref GetPoint(_state.Zp1, p2);
        Push(current ? ProjectCurrent(first, second) : ProjectOriginal(_state.Zp0, first, _state.Zp1, second));
    }

    private void FlipPoints(ref GlyphBudget budget)
    {
        var count = TakeLoop();
        for (var i = 0; i < count; i++)
        {
            budget.Charge(1);
            GetPoint(_state.Zp0, Pop()).Flags ^= HintPoint.OnCurve;
        }
    }

    private void FlipRange(bool onCurve, ref GlyphBudget budget)
    {
        var high = Pop();
        var low = Pop();
        if (low > high)
            return;

        GetPoint(_state.Zp0, high);
        GetPoint(_state.Zp0, low);
        var points = GetZone(_state.Zp0);
        for (var i = low; i <= high; i++)
        {
            budget.Charge(1);
            if (onCurve)
            {
                points[i].Flags |= HintPoint.OnCurve;
            }
            else
            {
                points[i].Flags &= ~HintPoint.OnCurve;
            }
        }
    }

    // ---- Deltas ------------------------------------------------------------------------------------------------

    /// <summary>Gets the distance of a delta exception at the current size, or <see langword="false"/> when it is for another size.</summary>
    private bool TryGetDelta(int argument, int rangeOffset, out int distance)
    {
        distance = 0;
        var size = ((argument >> 4) & 0x0F) + _state.DeltaBase + rangeOffset;
        if (size != PixelsPerEm)
            return false;

        var steps = (argument & 0x0F) - 8;
        if (steps >= 0)
        {
            steps++;
        }

        distance = steps * 64 / (1 << _state.DeltaShift);
        return true;
    }

    private void DeltaPoints(int rangeOffset, ref GlyphBudget budget)
    {
        var count = Pop();
        for (var i = 0; i < count; i++)
        {
            budget.Charge(1);
            var index = Pop();
            var argument = Pop();
            if (TryGetDelta(argument, rangeOffset, out var distance))
            {
                Move(ref GetPoint(_state.Zp0, index), distance);
            }
        }
    }

    private void DeltaControlValues(int rangeOffset, ref GlyphBudget budget)
    {
        var count = Pop();
        for (var i = 0; i < count; i++)
        {
            budget.Charge(1);
            var index = Pop();
            var argument = Pop();
            if (TryGetDelta(argument, rangeOffset, out var distance))
            {
                if ((uint)index >= (uint)_cvtCount)
                    throw Error("DELTAC changes a control value that does not exist.");

                ControlValues[index] += distance;
            }
        }
    }

    /// <summary>A range of instructions: a function or an instruction definition, which ends with its ENDF.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct CodeRange(int Start, int End)
    {
        public bool IsDefined => End > 0;
    }

    [StructLayout(LayoutKind.Auto)]
    private record struct CallFrame(int ReturnPosition, int ReturnStart, int ReturnEnd, CodeRange Range, int Count);
}
