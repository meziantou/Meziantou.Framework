using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Where the bins of the syntax elements of the slice segment data go: nowhere, to a rate, or to the arithmetic encoder
/// (<see cref="HevcBinSinkMode"/>). A syntax element is written once, by a method that gives its bins to the sink with the
/// context variables that its parsing reads (Rec. ITU-T H.265, clause 9.3.4.2): the same method measures a candidate of
/// the search and writes the decision that was made.
/// </summary>
/// <remarks>
/// The context variables are not in the sink: they are those of the decoder (<see cref="HevcCabacDecoder.Contexts"/>),
/// initialized, saved and restored with its methods. A bin updates its context variable in every mode but
/// <see cref="HevcBinSinkMode.None"/>, when <see cref="AdaptsContexts"/> is set: the rate of a candidate is then the
/// one of the state that the bins before it left, and the search saves the context variables before a candidate and
/// restores them after it.
/// </remarks>
internal sealed class HevcBinSink
{
    /// <summary>Gets the arithmetic encoder that the bins go to in the mode <see cref="HevcBinSinkMode.Encode"/>.</summary>
    public HevcCabacEncoder Encoder { get; } = new();

    /// <summary>Gets or sets what is done with the bins.</summary>
    public HevcBinSinkMode Mode { get; set; }

    /// <summary>Gets or sets the sum of the costs of the bins given in the mode <see cref="HevcBinSinkMode.Cost"/>, in units of 1/256 bit.</summary>
    public long Rate { get; set; }

    /// <summary>
    /// Gets or sets whether a bin whose cost is measured updates its context variable, as its encoding does. The default
    /// is <see langword="true"/>.
    /// </summary>
    public bool AdaptsContexts { get; set; } = true;

    /// <summary>Gives a bin coded with a context variable (the counterpart of <c>DecodeDecision</c>).</summary>
    /// <param name="context">The context variable: an element of <see cref="HevcCabacDecoder.Contexts"/>.</param>
    /// <param name="binVal">The bin: 0 or 1.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EncodeDecision(ref byte context, int binVal)
    {
        switch (Mode)
        {
            case HevcBinSinkMode.Cost:
                Rate += HevcCost.GetBinCost(context, binVal);
                if (AdaptsContexts)
                {
                    HevcCabacEncoder.UpdateContext(ref context, binVal);
                }

                break;

            case HevcBinSinkMode.Encode:
                Encoder.EncodeDecision(ref context, binVal);
                break;
        }
    }

    /// <summary>Gives a bin coded in bypass mode (the counterpart of <c>DecodeBypass</c>).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EncodeBypass(int binVal)
    {
        switch (Mode)
        {
            case HevcBinSinkMode.Cost:
                Rate += HevcCost.Scale;
                break;

            case HevcBinSinkMode.Encode:
                Encoder.EncodeBypass(binVal);
                break;
        }
    }

    /// <summary>
    /// Gives <paramref name="count"/> bins coded in bypass mode, the first one being the most significant bit of
    /// <paramref name="value"/>: a fixed-length binarization (clause 9.3.3.5), or the bins of any bypass coded bin string.
    /// </summary>
    public void EncodeBypassBits(uint value, int count)
    {
        switch (Mode)
        {
            case HevcBinSinkMode.Cost:
                Rate += HevcCost.GetBypassCost(count);
                break;

            case HevcBinSinkMode.Encode:
                Encoder.EncodeBypassBits(value, count);
                break;
        }
    }

    /// <summary>
    /// Gives the bins of the k-th order Exp-Golomb binarization of <paramref name="value"/> (clause 9.3.3.3), coded in
    /// bypass mode.
    /// </summary>
    public void EncodeExpGolomb(uint value, int k)
    {
        Debug.Assert(k is >= 0 and < 31 && value < 1u << 31);
        switch (Mode)
        {
            case HevcBinSinkMode.Cost:
                Rate += HevcCost.GetExpGolombCost(value, k);
                break;

            case HevcBinSinkMode.Encode:
                // Equation 9-13
                while (value >= 1u << k)
                {
                    Encoder.EncodeBypass(1);
                    value -= 1u << k;
                    k++;
                }

                Encoder.EncodeBypass(0);
                Encoder.EncodeBypassBits(value, k);
                break;
        }
    }

    /// <summary>
    /// Gives a bin coded before termination (the counterpart of <c>DecodeTerminate</c>): <c>end_of_slice_segment_flag</c>,
    /// <c>end_of_subset_one_bit</c> and <c>pcm_flag</c>. In the mode <see cref="HevcBinSinkMode.Encode"/>, a bin equal to 1
    /// ends the codeword of the arithmetic encoder.
    /// </summary>
    public void EncodeTerminate(int binVal)
    {
        switch (Mode)
        {
            case HevcBinSinkMode.Cost:
                Rate += HevcCost.GetTerminateCost(binVal);
                break;

            case HevcBinSinkMode.Encode:
                Encoder.EncodeTerminate(binVal);
                break;
        }
    }
}
