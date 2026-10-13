using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Hevc;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The arithmetic decoding engine and the initialization of its context variables (Rec. ITU-T H.265, clauses 9.3.2.2,
/// 9.3.2.6 and 9.3.4.3). The codewords are written by the arithmetic encoder of the test harness, which follows the
/// informative clause 9.3.5 and shares no code with the library.
/// </summary>
public sealed class HevcCabacDecoderTests
{
    [Fact]
    public void ContextVariablesAreInitializedFromInitValueAndSliceQp()
    {
        // initValue 154: slopeIdx 9 and offsetIdx 10, m = 0 and n = 64, preCtxState = 64 for every quantization parameter:
        // valMps 1, pStateIdx 0
        var decoder = new HevcCabacDecoder();
        decoder.InitializeContexts(0, 30);
        Assert.Equal((0 << 1) | 1, decoder.Contexts[HevcContextLayout.CuTransquantBypassFlag]);

        // initValue 153 (sao_merge_left_flag): m = 0 and n = 56: valMps 0, pStateIdx 63 - 56 = 7
        Assert.Equal(7 << 1, decoder.Contexts[HevcContextLayout.SaoMergeFlag]);

        // initValue 139 (the first context of split_cu_flag for initType 0): slopeIdx 8 and offsetIdx 11, m = -5 and
        // n = 72. SliceQpY 30: ( -150 >> 4 ) + 72 = -10 + 72 = 62: valMps 0, pStateIdx 1.
        Assert.Equal(1 << 1, decoder.Contexts[HevcContextLayout.SplitCuFlag]);

        // SliceQpY 0: 72: valMps 1, pStateIdx 8. SliceQpY 51: ( -255 >> 4 ) + 72 = -16 + 72 = 56: valMps 0, pStateIdx 7.
        decoder.InitializeContexts(0, 0);
        Assert.Equal((8 << 1) | 1, decoder.Contexts[HevcContextLayout.SplitCuFlag]);
        decoder.InitializeContexts(0, 51);
        Assert.Equal(7 << 1, decoder.Contexts[HevcContextLayout.SplitCuFlag]);

        // SliceQpY is clipped to the range 0 to 51
        decoder.InitializeContexts(0, -12);
        Assert.Equal((8 << 1) | 1, decoder.Contexts[HevcContextLayout.SplitCuFlag]);

        // The three initialization types: the first context of split_cu_flag has the values 139, 107 and 107 (Table 9-7).
        // initValue 107: slopeIdx 6 and offsetIdx 11, m = -15 and n = 72; SliceQpY 26: ( -390 >> 4 ) + 72 = -25 + 72 = 47:
        // valMps 0, pStateIdx 16
        decoder.InitializeContexts(1, 26);
        Assert.Equal(16 << 1, decoder.Contexts[HevcContextLayout.SplitCuFlag]);
        decoder.InitializeContexts(2, 26);
        Assert.Equal(16 << 1, decoder.Contexts[HevcContextLayout.SplitCuFlag]);

        // Every context of every initialization type is the one of the definition
        for (var initType = 0; initType < 3; initType++)
        {
            var initValues = initType switch
            {
                0 => HevcCabacTables.InitValues0,
                1 => HevcCabacTables.InitValues1,
                _ => HevcCabacTables.InitValues2,
            };
            Assert.HasCount(HevcContextLayout.Count, initValues.ToArray());
            for (var qp = -3; qp <= 54; qp++)
            {
                decoder.InitializeContexts(initType, qp);
                for (var i = 0; i < initValues.Length; i++)
                {
                    Assert.Equal(ReferenceHevcCabacEncoder.GetInitialState(initValues[i], qp), decoder.Contexts[i]);
                }
            }
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void BinsOfEveryKindAreDecodedAsTheyWereEncoded()
    {
        for (var seed = 1; seed <= 40; seed++)
        {
            var random = new Random(seed);
            var encoder = new ReferenceHevcCabacEncoder();
            var decoder = new HevcCabacDecoder();
            var sliceQpY = random.Next(0, 52);
            var initType = random.Next(3);
            decoder.InitializeContexts(initType, sliceQpY);
            var states = decoder.Contexts.ToArray();

            // A skew per context, so that the states move in both directions; the kinds of bins are mixed
            var skew = new int[states.Length];
            for (var i = 0; i < skew.Length; i++)
            {
                skew[i] = random.Next(1, 100);
            }

            var count = random.Next(1, 4000);
            var bins = new List<(int Kind, int Context, int Value)>(count);
            for (var i = 0; i < count; i++)
            {
                var kind = random.Next(10) switch
                {
                    < 6 => 0,
                    < 9 => 1,
                    _ => 2,
                };
                var context = random.Next(states.Length);
                var value = kind switch
                {
                    0 => random.Next(100) < skew[context] ? 1 : 0,
                    1 => random.Next(2),
                    _ => 0,
                };
                bins.Add((kind, context, value));
                switch (kind)
                {
                    case 0:
                        encoder.EncodeDecision(ref states[context], value);
                        break;
                    case 1:
                        encoder.EncodeBypass(value);
                        break;
                    default:
                        encoder.EncodeTerminate(0);
                        break;
                }
            }

            encoder.EncodeTerminate(1);
            var codewordBits = encoder.BitCount;
            encoder.AlignToByte();
            var data = encoder.ToArray();
            decoder.Initialize(data, 0, data.Length);
            foreach (var (kind, context, value) in bins)
            {
                var decoded = kind switch
                {
                    0 => decoder.DecodeDecision(context),
                    1 => decoder.DecodeBypass(),
                    _ => decoder.DecodeTerminate(),
                };
                Assert.Equal(value, decoded);
            }

            Assert.Equal(1, decoder.DecodeTerminate());

            // The decoder is where the encoder stopped: after the bit equal to 1 that ends the codeword
            Assert.Equal(codewordBits, decoder.BitPosition);
            Assert.Equal(states, decoder.Contexts.ToArray());
        }
    }

    [Fact]
    public void ACodewordStartsAgainAfterATermination()
    {
        // pcm_flag, the PCM samples on a byte, then a new codeword
        var encoder = new ReferenceHevcCabacEncoder();
        var state = ReferenceHevcCabacEncoder.GetInitialState(154, 26);
        encoder.EncodeDecision(ref state, 1);
        encoder.EncodeBypass(1);
        encoder.EncodeTerminate(1);
        encoder.AlignToByte();
        var pcmPosition = (int)(encoder.BitCount / 8);
        encoder.WriteBits(0xA5, 8);
        encoder.WriteBits(0x3C, 8);
        encoder.Restart();
        encoder.EncodeBypass(0);
        encoder.EncodeBypass(1);
        encoder.EncodeBypass(1);
        encoder.EncodeTerminate(1);
        encoder.AlignToByte();
        var data = encoder.ToArray();

        var decoder = new HevcCabacDecoder();
        decoder.InitializeContexts(0, 26);
        decoder.Initialize(data, 0, data.Length);
        Assert.Equal(1, decoder.DecodeDecision(HevcContextLayout.CuTransquantBypassFlag));
        Assert.Equal(1, decoder.DecodeBypass());
        Assert.Equal(1, decoder.DecodeTerminate());
        Assert.Equal(pcmPosition, (int)((decoder.BitPosition + 7) / 8));
        Assert.Equal(new byte[] { 0xA5, 0x3C }, data.AsSpan(pcmPosition, 2).ToArray());
        decoder.Initialize(data, pcmPosition + 2, data.Length);
        Assert.Equal(0b011, decoder.DecodeBypassBits(3));
        Assert.Equal(1, decoder.DecodeTerminate());
        Assert.Equal((long)data.Length * 8, ((decoder.BitPosition + 7) / 8) * 8);
    }

    [Fact]
    public void ReadingPastTheEndOfTheDataIsAnError()
    {
        // A codeword needs 9 bits
        var decoder = new HevcCabacDecoder();
        decoder.InitializeContexts(0, 26);
        Assert.Throws<InvalidImageContentException>(() => decoder.Initialize([0x12], 0, 1));
        Assert.Throws<InvalidImageContentException>(() => decoder.Initialize([0x12, 0x34, 0x56], 3, 3));

        // ivlOffset is 510 or 511: not the start of a codeword (clause 9.3.2.6)
        Assert.Throws<InvalidImageContentException>(() => decoder.Initialize([0xFF, 0x00], 0, 2));
        Assert.Throws<InvalidImageContentException>(() => decoder.Initialize([0xFF, 0x80], 0, 2));

        // Bypass bins read one bit each: 7 bits are left after the first 9 bits of two bytes
        decoder.Initialize([0x00, 0x00], 0, 2);
        for (var i = 0; i < 7; i++)
        {
            Assert.Equal(0, decoder.DecodeBypass());
        }

        Assert.Equal(16, decoder.BitPosition);
        var exception = Assert.Throws<InvalidImageContentException>(() => decoder.DecodeBypass());
        Assert.Equal(ImageFormat.Heic, exception.Format);

        // A codeword of zero bits never terminates: the decoding of decisions ends with the data too
        decoder.Initialize(new byte[4], 0, 4);
        Assert.Throws<InvalidImageContentException>(() =>
        {
            for (var i = 0; i < 100_000; i++)
            {
                decoder.DecodeDecision(HevcContextLayout.SigCoeffFlag);
            }
        });

        // A termination whose last bit is not 1 is not the end of a codeword: ivlOffset 508 of ivlCurrRange 510
        decoder.Initialize([0xFE, 0x00], 0, 2);
        Assert.Throws<InvalidImageContentException>(() => decoder.DecodeTerminate());
        decoder.Initialize([0xFE, 0x80], 0, 2);
        Assert.Equal(1, decoder.DecodeTerminate());
        Assert.Equal(9, decoder.BitPosition);
    }
}
