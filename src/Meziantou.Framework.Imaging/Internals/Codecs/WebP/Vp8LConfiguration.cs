using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A choice of transforms for the main image.</summary>
/// <param name="Mode">The transforms.</param>
/// <param name="TransformBits">The tile size bits of the predictor and color transforms (2 to 6).</param>
/// <param name="EntropyPredictors">Whether the predictor modes are chosen by the entropy of the residuals instead of their sum of absolute values.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct Vp8LConfiguration(Vp8LTransformMode Mode, int TransformBits, bool EntropyPredictors);
