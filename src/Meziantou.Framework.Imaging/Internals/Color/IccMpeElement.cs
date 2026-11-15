using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A processing element of a <c>multiProcessElementsType</c> tag (ICC.1:2022 section 10.16, ICC.2:2023 clause 11): a
/// function of <see cref="InputCount"/> floating-point values to <see cref="OutputCount"/> values. Elements are immutable
/// and evaluated in <see cref="double"/>, except the calculator element (single precision, see <see cref="IccMpeCalculator"/>).
/// </summary>
internal abstract class IccMpeElement
{
    protected IccMpeElement(int inputCount, int outputCount)
    {
        InputCount = inputCount;
        OutputCount = outputCount;
    }

    /// <summary>Gets the number of input channels.</summary>
    public int InputCount { get; }

    /// <summary>Gets the number of output channels.</summary>
    public int OutputCount { get; }

    /// <summary>Gets the number of values of the scratch memory <see cref="Apply"/> needs.</summary>
    public virtual int ScratchSize => 0;

    /// <summary>Gets an upper bound of the operations of one evaluation, which bounds the cost of nested calculator elements.</summary>
    public virtual long Cost => InputCount + OutputCount;

    /// <summary>Evaluates the element.</summary>
    /// <param name="input">The <see cref="InputCount"/> input values.</param>
    /// <param name="output">Receives the <see cref="OutputCount"/> output values; it does not overlap <paramref name="input"/>.</param>
    /// <param name="scratch">At least <see cref="ScratchSize"/> values the element may use.</param>
    public abstract void Apply(ReadOnlySpan<double> input, Span<double> output, Span<double> scratch);
}
