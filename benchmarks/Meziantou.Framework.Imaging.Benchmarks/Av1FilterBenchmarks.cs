using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// The AV1 post-processing filters on their own, at sizes that the decoded corpus of <see cref="AvifBenchmarks"/> is too small
/// to weigh: the self-guided loop restoration of 64x64 blocks with every parameter set, the film grain synthesis of a
/// 640x480 4:2:0 frame (the grain templates, then the grain of every stripe), the CDEF of the 4x4 chroma blocks of 4:2:0
/// frames, and the deblocking of pairs of consecutive edges, together and one edge at a time. Inputs are seeded random data;
/// <c>VectorizedKernelTests</c> checks the vector paths against their scalar references.
/// </summary>
[MemoryDiagnoser]
public sealed class Av1FilterBenchmarks : IDisposable
{
    private const int GrainWidth = 640;
    private const int GrainHeight = 480;
    private const int CdefBlocks = 1024;
    private const int CdefStride = 4 + 4;
    private const int DeblockStride = 64;
    private const int DeblockEdges = 256;
    private const int BlockSize = Av1LoopRestoration.MaxBlockWidth;
    private const int SetCount = 16;
    private const int SourceStride = BlockSize + (2 * Av1LoopRestoration.Padding);
    private readonly ushort[] _source8 = new ushort[SourceStride * SourceStride];
    private readonly ushort[] _source10 = new ushort[SourceStride * SourceStride];
    private readonly ushort[] _destination = new ushort[BlockSize * BlockSize];
    private readonly int[] _scratch = new int[Av1LoopRestoration.ScratchLength];
    private readonly sbyte[][] _units = new sbyte[SetCount][];
    private readonly AllocationScope _scope = new(new ImageResourceLimits());
    private readonly ushort[][] _grainOutput = [new ushort[GrainWidth * GrainHeight], new ushort[GrainWidth * GrainHeight / 4], new ushort[GrainWidth * GrainHeight / 4]];
    private Av1FrameBuffer _frame8 = null!;
    private Av1FrameBuffer _frame10 = null!;
    private Av1FilmGrainParameters _grain = null!;
    private readonly ushort[] _cdefWindows = new ushort[CdefBlocks * (4 + 4) * CdefStride];
    private readonly ushort[] _cdefOutput = new ushort[CdefBlocks * 4 * CdefStride];
    private readonly int[][] _cdefOffsets = new int[8][];
    private readonly ushort[] _deblockSource = new ushort[DeblockStride * DeblockStride];
    private readonly ushort[] _deblockSamples = new ushort[DeblockStride * DeblockStride];

    [GlobalSetup]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded benchmark inputs.")]
    public void Setup()
    {
        // A textured block: a smooth ramp with noise, so that the boxes have every variance
        var random = new Random(42);
        for (var i = 0; i < _source8.Length; i++)
        {
            var ramp = (i % SourceStride) + (i / SourceStride);
            _source8[i] = (ushort)Math.Clamp((ramp * 2) + random.Next(-12, 13), 0, 255);
            _source10[i] = (ushort)Math.Clamp((ramp * 8) + random.Next(-48, 49), 0, 1023);
        }

        // One restoration unit per self-guided set, with the default projection weights
        for (var set = 0; set < SetCount; set++)
        {
            _units[set] = [Av1Constants.RestoreSgrproj, 0, 0, 0, 0, 0, 0, (sbyte)set, Av1Tables.SgrprojXqdMid[0], Av1Tables.SgrprojXqdMid[1]];
        }

        // The chroma blocks of 4:2:0 frames: the window of a block has 2 rows and 2 columns around it, and the taps of every
        // direction
        for (var i = 0; i < _cdefWindows.Length; i++)
        {
            _cdefWindows[i] = (ushort)Math.Clamp(128 + random.Next(-20, 21), 0, 255);
        }

        for (var direction = 0; direction < 8; direction++)
        {
            _cdefOffsets[direction] = GetCdefTapOffsets(direction);
        }

        // Smooth areas with steps: most lines are filtered, by every filter
        for (var y = 0; y < DeblockStride; y++)
        {
            for (var x = 0; x < DeblockStride; x++)
            {
                _deblockSource[(y * DeblockStride) + x] = (ushort)Math.Clamp(100 + (((x / 8) + (y / 8)) % 3 * 6) + random.Next(-1, 2), 0, 255);
            }
        }

        // Grain on the 3 planes, with the largest auto-regression lag and overlapping blocks: the parameters of a typical
        // encoder estimate
        _frame8 = CreateFrame(random, bitDepth: 8);
        _frame10 = CreateFrame(random, bitDepth: 10);
        var lag = 3;
        var positions = 2 * lag * (lag + 1);
        _grain = new Av1FilmGrainParameters
        {
            GrainSeed = 4321,
            NumYPoints = 6,
            PointYValue = [0, 40, 80, 128, 192, 255],
            PointYScaling = [20, 36, 48, 52, 40, 24],
            NumCbPoints = 3,
            PointCbValue = [0, 128, 255],
            PointCbScaling = [16, 24, 16],
            NumCrPoints = 3,
            PointCrValue = [0, 128, 255],
            PointCrScaling = [12, 20, 12],
            GrainScaling = 11,
            ArCoeffLag = lag,
            ArCoeffsY = [.. Enumerable.Range(0, positions).Select(_ => (sbyte)random.Next(-24, 25))],
            ArCoeffsCb = [.. Enumerable.Range(0, positions + 1).Select(_ => (sbyte)random.Next(-24, 25))],
            ArCoeffsCr = [.. Enumerable.Range(0, positions + 1).Select(_ => (sbyte)random.Next(-24, 25))],
            ArCoeffShift = 7,
            GrainScaleShift = 0,
            CbMult = 128 + 32,
            CbLumaMult = 128 + 64,
            CbOffset = 256,
            CrMult = 128 + 32,
            CrLumaMult = 128 + 64,
            CrOffset = 256,
            OverlapFlag = true,
        };
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _frame8?.Release();
        _frame10?.Release();
        _frame8 = null!;
        _frame10 = null!;
    }

    [Benchmark(OperationsPerInvoke = SetCount)]
    public void SelfGuided8() => SelfGuided(_source8, bitDepth: 8);

    [Benchmark(OperationsPerInvoke = SetCount)]
    public void SelfGuided10() => SelfGuided(_source10, bitDepth: 10);

    [Benchmark]
    public void FilmGrain8() => ApplyGrain(_frame8);

    [Benchmark]
    public void FilmGrain10() => ApplyGrain(_frame10);

    [Benchmark(OperationsPerInvoke = CdefBlocks)]
    public void CdefChroma()
    {
        const int WindowLength = (4 + 4) * CdefStride;
        for (var block = 0; block < CdefBlocks; block++)
        {
            // Strengths of a typical frame at 8 bits: primary 2 to 5, secondary 1 or 2, the damping of the chroma (5 - 1)
            var priStr = 2 + (block & 3);
            var secStr = 1 + ((block >> 2) & 1);
            var priShift = Math.Max(0, 4 - System.Numerics.BitOperations.Log2((uint)priStr));
            var secShift = Math.Max(0, 4 - System.Numerics.BitOperations.Log2((uint)secStr));
            Av1Cdef.FilterInteriorRows(_cdefOutput.AsSpan(block * 4 * CdefStride, 4 * CdefStride), _cdefWindows.AsSpan(block * WindowLength, WindowLength), CdefStride, 4, 4, _cdefOffsets[block & 7], priStr, secStr, priShift, secShift, (priStr & 1) is 0 ? 4 : 3, (priStr & 1) is 0 ? 2 : 3, 2, 1);
        }
    }

    [Benchmark(OperationsPerInvoke = DeblockEdges)]
    public void DeblockEdgePairs() => Deblock(paired: true);

    [Benchmark(OperationsPerInvoke = DeblockEdges)]
    public void DeblockEdgesOneByOne() => Deblock(paired: false);

    public void Dispose() => Cleanup();

    // Pairs of vertical and horizontal luma edges of the 3 filter sizes, inside the area (8 samples from its sides), level 32
    private void Deblock(bool paired)
    {
        _deblockSource.CopyTo(_deblockSamples, 0);
        for (var edge = 0; edge < DeblockEdges; edge++)
        {
            var vertical = (edge & 1) is 0;
            var filterSize = 4 << ((edge >> 1) % 3);
            var along = 8 + ((edge >> 3) % 6 * 8);
            var across = 8 + ((edge >> 2) % 6 * 8);
            var position = vertical ? (along * DeblockStride) + across : (across * DeblockStride) + along;
            var (step, lineStep) = vertical ? (1, DeblockStride) : (DeblockStride, 1);
            if (paired)
            {
                Av1LoopFilter.FilterLinePairs(_deblockSamples, position, step, lineStep, 0, filterSize, 8, 8, 76, 2, 8, 76, 2);
            }
            else
            {
                Av1LoopFilter.FilterLines(_deblockSamples, position, step, lineStep, 0, filterSize, 8, 8, 76, 2);
                Av1LoopFilter.FilterLines(_deblockSamples, position + (4 * lineStep), step, lineStep, 0, filterSize, 8, 8, 76, 2);
            }
        }
    }

    // The offsets of the taps of a direction in the window (section 7.15.3): 4 primary taps, then 8 secondary taps
    private static int[] GetCdefTapOffsets(int direction)
    {
        var offsets = new int[12];
        for (var k = 0; k < 2; k++)
        {
            offsets[2 * k] = Offset(direction, k);
            offsets[(2 * k) + 1] = -offsets[2 * k];
            offsets[4 + (4 * k)] = Offset((direction + 6) & 7, k);
            offsets[5 + (4 * k)] = -offsets[4 + (4 * k)];
            offsets[6 + (4 * k)] = Offset((direction + 2) & 7, k);
            offsets[7 + (4 * k)] = -offsets[6 + (4 * k)];
        }

        return offsets;

        static int Offset(int d, int k) => (Av1Tables.CdefDirections[((d * 2) + k) * 2] * CdefStride) + Av1Tables.CdefDirections[(((d * 2) + k) * 2) + 1];
    }

    private void ApplyGrain(Av1FrameBuffer frame)
    {
        using var grain = new Av1FilmGrain(_scope, frame.Sequence, _grain, GrainWidth, GrainHeight);
        const int ChromaWidth = GrainWidth / 2;
        for (var stripe = 0; stripe < grain.StripeCount; stripe++)
        {
            var chromaOffset = stripe * (Av1FilmGrain.StripeHeight / 2) * ChromaWidth;
            grain.ApplyStripe(frame, stripe, _grainOutput[0].AsSpan(stripe * Av1FilmGrain.StripeHeight * GrainWidth), GrainWidth, _grainOutput[1].AsSpan(chromaOffset), _grainOutput[2].AsSpan(chromaOffset), ChromaWidth);
        }
    }

    // A 4:2:0 frame of noisy ramps
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded benchmark inputs.")]
    private Av1FrameBuffer CreateFrame(Random random, int bitDepth)
    {
        var parameters = new Av1SequenceParameters(GrainWidth, GrainHeight, bitDepth, Monochrome: false, SubsamplingX: 1, SubsamplingY: 1, ColorPrimaries: 1, TransferCharacteristics: 13, MatrixCoefficients: 6, FullRange: true, ChromaSamplePosition: 0, StillPicture: true, Use128x128Superblock: false, EnableCdef: true, EnableRestoration: true, Av1Levels.GetLevelIndex(GrainWidth, GrainHeight));
        using var header = new PooledPayloadWriter(_scope);
        Av1HeaderWriter.WriteSequenceHeader(header, parameters);
        var frame = new Av1FrameBuffer(Av1SequenceHeader.Parse(header.WrittenSpan, operatingPoint: 0), new Av1FrameHeader { FrameWidth = GrainWidth, FrameHeight = GrainHeight, UpscaledWidth = GrainWidth });
        frame.Allocate(_scope);
        var maxSample = (1 << bitDepth) - 1;
        for (var plane = 0; plane < 3; plane++)
        {
            var samples = frame.GetPlane(plane);
            for (var y = 0; y < samples.Height; y++)
            {
                var row = samples.GetRow(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = (ushort)Math.Clamp(((x + y) * maxSample / (samples.Width + samples.Height)) + random.Next(-maxSample / 32, (maxSample / 32) + 1), 0, maxSample);
                }
            }
        }

        return frame;
    }

    private void SelfGuided(ushort[] source, int bitDepth)
    {
        const int Offset = (Av1LoopRestoration.Padding * SourceStride) + Av1LoopRestoration.Padding;
        foreach (var unit in _units)
        {
            Av1LoopRestoration.ApplySelfGuided(source, SourceStride, Offset, _destination, BlockSize, BlockSize, BlockSize, unit, bitDepth, _scratch);
        }
    }
}
