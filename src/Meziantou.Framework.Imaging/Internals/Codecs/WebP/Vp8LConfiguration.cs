using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A choice of transforms for the main image.</summary>
/// <param name="Mode">The transforms.</param>
/// <param name="TransformBits">
/// The tile size bits of the predictor transform (2 to 6), and of the color transform of an alpha plane; the color transform
/// of a main image uses tiles twice as large, from 16 to 64 pixels.
/// </param>
/// <param name="EntropyPredictors">
/// Whether the predictor modes of an alpha plane are chosen by the entropy of the residuals in the tile instead of their sum
/// of absolute values (see <see cref="Vp8LPredictorSearch.AllModes"/>).
/// </param>
/// <param name="Flat">
/// Whether at least half the pixels repeat their left or top neighbor: the parses of other images only search the left and
/// top neighbors (<see cref="Vp8LEncoderTools"/>).
/// </param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct Vp8LConfiguration(Vp8LTransformMode Mode, int TransformBits, bool EntropyPredictors, bool Flat = true);
