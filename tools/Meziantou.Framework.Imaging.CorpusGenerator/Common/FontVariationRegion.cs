namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>A region of the variation space: for each axis, where its deltas start to apply, apply fully and stop applying.</summary>
internal sealed record FontVariationRegion(params (double Start, double Peak, double End)[] Axes);
