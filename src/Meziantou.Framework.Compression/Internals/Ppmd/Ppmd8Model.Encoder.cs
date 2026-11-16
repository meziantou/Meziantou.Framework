// PPMd var.I rev. 1 encoder, a port of 7-Zip's Ppmd8Enc.c (Igor Pavlov, public domain), which is based on PPMd var.I (2002)
// by Dmitry Shkarin (public domain). It updates the model exactly as the decoder does, so that the decoder of
// Ppmd8Model.cs and the one of 7-Zip read what it writes.
namespace Meziantou.Framework.Compression.Internals.Ppmd;

internal sealed partial class Ppmd8Model
{
    /// <summary>Encodes a symbol (Ppmd8_EncodeSymbol): a byte, or -1 for the end marker (an escape down to the order-0 context).</summary>
    public void EncodeSymbol(ref PpmdCarrylessRangeEncoder rc, int symbol)
    {
        ref var mc = ref Ctx(_minContext);
        if (mc.NumStats != 0)
        {
            var s = mc.Stats;
            ref var state = ref St(s);
            uint summFreq = mc.SummFreq;
            if (state.Symbol == symbol)
            {
                rc.Encode(0, state.Freq, summFreq);
                _foundState = s;
                Update1_0();
                return;
            }

            _prevSuccess = 0;
            uint sum = state.Freq;
            int i = mc.NumStats;
            do
            {
                s += StateSize;
                state = ref St(s);
                if (state.Symbol == symbol)
                {
                    rc.Encode(sum, state.Freq, summFreq);
                    _foundState = s;
                    Update1();
                    return;
                }

                sum += state.Freq;
            }
            while (--i > 0);

            rc.Encode(sum, summFreq - sum, summFreq);
            ResetMask();
            for (uint j = 0; j <= mc.NumStats; j++)
            {
                Mask(St(mc.Stats + (j * StateSize)).Symbol);
            }
        }
        else
        {
            ref var prob = ref GetBinSumm();
            uint size0 = prob;
            ref var oneState = ref St(_minContext + 2);
            if (oneState.Symbol == symbol)
            {
                rc.EncodeBit0(size0);
                prob = (ushort)(prob + (1 << IntBits) - GetMean(prob));
                _foundState = _minContext + 2;
                UpdateBin();
                return;
            }

            rc.EncodeBit1(size0);
            prob = (ushort)(prob - GetMean(prob));
            _initEsc = ExpEscape[(prob >> 10) & 15];
            ResetMask();
            Mask(oneState.Symbol);
            _prevSuccess = 0;
        }

        while (true)
        {
            uint numMasked = Ctx(_minContext).NumStats;
            do
            {
                _orderFall++;
                var suffix = Ctx(_minContext).Suffix;
                if (suffix == 0)
                    return; // The end marker

                _minContext = suffix;
            }
            while (Ctx(_minContext).NumStats == numMasked);

            ref var see = ref MakeEscFreq(numMasked, out var escFreq);
            mc = ref Ctx(_minContext);
            var stats = mc.Stats;
            var count = (uint)mc.NumStats + 1;
            uint sum = 0;
            for (uint j = 0; j < count; j++)
            {
                var s = stats + (j * StateSize);
                ref var state = ref St(s);
                if (IsMasked(state.Symbol))
                    continue;

                if (state.Symbol == symbol)
                {
                    // The total is the escape frequency and the frequencies of all the symbols that are not masked
                    var low = sum;
                    uint freq = state.Freq;
                    var total = sum + escFreq;
                    for (var k = j; k < count; k++)
                    {
                        ref var other = ref St(stats + (k * StateSize));
                        if (!IsMasked(other.Symbol))
                        {
                            total += other.Freq;
                        }
                    }

                    rc.Encode(low, freq, total);
                    see.Update();
                    _foundState = s;
                    Update2();
                    return;
                }

                sum += state.Freq;
            }

            var escapeTotal = sum + escFreq;
            see.Summ = (ushort)(see.Summ + escapeTotal);
            rc.Encode(sum, escapeTotal - sum, escapeTotal);
            for (uint j = 0; j < count; j++)
            {
                Mask(St(stats + (j * StateSize)).Symbol);
            }
        }
    }
}
