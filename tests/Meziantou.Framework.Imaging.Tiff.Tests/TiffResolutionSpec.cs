namespace Meziantou.Framework.Imaging.Tests;

/// <summary>The resolution tags of a page assembled by <see cref="TiffFileBuilder"/>.</summary>
internal sealed record TiffResolutionSpec(uint XNumerator, uint YNumerator, uint Denominator, int Unit);
