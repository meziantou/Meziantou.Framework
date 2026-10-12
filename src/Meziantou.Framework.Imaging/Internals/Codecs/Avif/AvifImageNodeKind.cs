namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The way an image item of an AVIF file gets its pixels.</summary>
internal enum AvifImageNodeKind
{
    /// <summary>An image coded with AV1: an <c>av01</c> item, or a <c>grid</c> of <c>av01</c> items, with its alpha plane.</summary>
    Coded,

    /// <summary>An identity derivation (<c>iden</c>): its one input image.</summary>
    Identity,

    /// <summary>A grid (<c>grid</c>) whose tiles are not all coded images: its input images, side by side.</summary>
    Grid,

    /// <summary>An overlay (<c>iovl</c>): its input images, composited on a canvas.</summary>
    Overlay,
}
