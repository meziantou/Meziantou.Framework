namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The morphological operations of <see cref="MorphologyFilter"/>.</summary>
internal enum MorphologyOperation
{
    Erode,
    Dilate,
    Open,
    Close,
    TopHat,
    BlackHat,
}
