using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The stage that evaluates the processing elements of a <c>multiProcessElementsType</c> tag in sequence. Its input and
/// output are the channels of the pipeline (at most <see cref="IccPipeline.MaxChannels"/>); the channels between the
/// elements may be more. A value that is not a number at the end is replaced by 0.
/// </summary>
internal sealed class IccMpeStage : IccStage
{
    // Above this many values, the scratch memory of an evaluation is rented instead of allocated on the stack
    private const int StackScratchLimit = 256;

    private readonly IccMpeElement[] _elements;
    private readonly int _inputCount;
    private readonly int _outputCount;
    private readonly int _widest;
    private readonly int _scratchSize;

    public IccMpeStage(IccMpeElement[] elements, int inputCount, int outputCount, int widest, int scratchSize)
    {
        _elements = elements;
        _inputCount = inputCount;
        _outputCount = outputCount;
        _widest = widest;
        _scratchSize = scratchSize;
    }

    public override void Apply(Span<double> values)
    {
        var size = (2 * _widest) + _scratchSize;
        double[]? rented = null;
        var memory = size <= StackScratchLimit ? stackalloc double[StackScratchLimit] : (rented = System.Buffers.ArrayPool<double>.Shared.Rent(size));
        try
        {
            var current = memory[.._widest];
            var next = memory.Slice(_widest, _widest);
            var scratch = memory.Slice(2 * _widest, _scratchSize);
            values[.._inputCount].CopyTo(current);
            var count = _inputCount;
            foreach (var element in _elements)
            {
                element.Apply(current[..count], next[..element.OutputCount], scratch);
                count = element.OutputCount;
                var swap = current;
                current = next;
                next = swap;
            }

            for (var i = 0; i < _outputCount; i++)
            {
                var value = current[i];
                values[i] = double.IsNaN(value) ? 0 : value;
            }
        }
        finally
        {
            if (rented is not null)
            {
                System.Buffers.ArrayPool<double>.Shared.Return(rented);
            }
        }
    }
}
