namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>A frame or a layer of a JPEG XL file, as <c>jxlinfo</c> lists it.</summary>
/// <param name="IsLayer">Whether jxlinfo calls it a layer: a frame of zero duration that is composed with the next one.</param>
/// <param name="IsFullSize">Whether the frame covers the image exactly.</param>
/// <param name="Width">The width of the frame.</param>
/// <param name="Height">The height of the frame.</param>
/// <param name="X">The horizontal position of the frame on the image.</param>
/// <param name="Y">The vertical position of the frame on the image.</param>
/// <param name="DurationMilliseconds">The duration jxlinfo prints with one decimal, in milliseconds; <see langword="null"/> for a still image.</param>
/// <param name="Name">The name of the frame, or <see langword="null"/>.</param>
public sealed record JxlInfoFrame(bool IsLayer, bool IsFullSize, int Width, int Height, int X, int Y, double? DurationMilliseconds, string? Name);
