namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A callback receiving scoped access to the drawing surface of a frame or of an image.</summary>
/// <param name="canvas">The canvas, valid only until the callback returns.</param>
public delegate void DrawingAction(DrawingCanvas canvas);
