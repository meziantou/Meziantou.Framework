namespace Meziantou.Framework.Imaging.Internals;

/// <summary>How the pixels of an encoded frame region are combined with the canvas (APNG <c>blend_op</c>).</summary>
internal enum AnimationBlend
{
    /// <summary>The region pixels replace the canvas pixels, alpha included (APNG <c>APNG_BLEND_OP_SOURCE</c>).</summary>
    Source = 0,

    /// <summary>The region pixels are composited over the canvas with straight alpha (APNG <c>APNG_BLEND_OP_OVER</c>).</summary>
    Over = 1,
}
