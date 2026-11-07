using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The encoding of a frame chosen by <see cref="AnimationFrameDelta"/>.</summary>
/// <param name="PreviousDisposal">The disposal of the previous frame.</param>
/// <param name="PreviousRegion">The region of the previous frame (grown by <see cref="AnimationDeltaRules.EnlargePreviousRegion"/>), which <see cref="AnimationDisposal.ClearToTransparent"/> clears.</param>
/// <param name="Region">The region of the frame.</param>
/// <param name="Blend">The blending of the frame: with <see cref="AnimationBlend.Over"/>, its unchanged pixels are written fully transparent.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct AnimationFramePlan(AnimationDisposal PreviousDisposal, Rectangle PreviousRegion, Rectangle Region, AnimationBlend Blend);
