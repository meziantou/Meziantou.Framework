using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The preferred aspect ratio of a box (https://drafts.csswg.org/css-sizing-4/#aspect-ratio) as a transfer between its
/// border-box sizes in its own writing mode: the ratio applies to the content box, or to the box specified by
/// <c>box-sizing</c> for an <c>aspect-ratio: &lt;ratio&gt;</c>, so the paddings and the borders that are outside of that box
/// are removed before the transfer and added back after it.
/// </summary>
/// <param name="InlineOverBlock">The ratio of the inline size to the block size.</param>
/// <param name="InlineDelta">The paddings and borders of the inline axis outside of the box the ratio applies to.</param>
/// <param name="BlockDelta">The paddings and borders of the block axis outside of the box the ratio applies to.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct AspectRatioTransfer(double InlineOverBlock, double InlineDelta, double BlockDelta)
{
    /// <summary>Returns the border-box inline size that corresponds to a border-box block size.</summary>
    public double ToInline(double blockSize) => (Math.Max(0, blockSize - BlockDelta) * InlineOverBlock) + InlineDelta;

    /// <summary>Returns the border-box block size that corresponds to a border-box inline size.</summary>
    public double ToBlock(double inlineSize) => (Math.Max(0, inlineSize - InlineDelta) / InlineOverBlock) + BlockDelta;
}
