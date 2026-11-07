namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A callback receiving scoped access to the drawing surface of a frame or of an image, and caller state.</summary>
/// <typeparam name="TState">The type of the state.</typeparam>
/// <param name="canvas">The canvas, valid only until the callback returns.</param>
/// <param name="state">The state passed by the caller.</param>
public delegate void DrawingAction<TState>(DrawingCanvas canvas, TState state)
    where TState : allows ref struct;
