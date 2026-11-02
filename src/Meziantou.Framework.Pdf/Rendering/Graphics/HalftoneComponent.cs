namespace Meziantou.Framework.Pdf.Rendering.Graphics;

/// <summary>The screen of a component, and its transfer function (<c>/TransferFunction</c>), which replaces the one of the graphics state; <see langword="null"/> for the one of the graphics state.</summary>
internal sealed record HalftoneComponent(HalftoneScreen Screen, Imaging.Drawing.TransferFunction? Transfer);
