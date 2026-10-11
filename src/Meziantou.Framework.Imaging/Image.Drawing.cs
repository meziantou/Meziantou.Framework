using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

public abstract partial class Image
{
    /// <summary>Draws on every frame of the image and on the poster frame: each shape is painted identically on all of them.</summary>
    /// <remarks>
    /// The callback runs once. See <see cref="DrawingCanvas"/> for the coordinates and the rendering rules, and
    /// <see cref="ImageFrame.Draw(DrawingAction, CancellationToken)"/> to draw on a single frame.
    /// </remarks>
    /// <param name="action">The callback that draws. It must not be asynchronous and must not retain the canvas.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. It is checked by every drawing call.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled. The shapes already drawn stay, and some rows of the one being drawn may be painted, the same rows on every frame.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public void Draw(DrawingAction action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        using var session = BeginDrawing(cancellationToken);
        action(new DrawingCanvas(session));
    }

    /// <summary>Draws on every frame of the image and on the poster frame, passing caller state to avoid closure allocations.</summary>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="state">The state passed to <paramref name="action"/>.</param>
    /// <param name="action">The callback that draws. Use a static lambda to avoid allocations. It must not retain the canvas.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. It is checked by every drawing call.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled. The shapes already drawn stay, and some rows of the one being drawn may be painted, the same rows on every frame.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public void Draw<TState>(TState state, DrawingAction<TState> action, CancellationToken cancellationToken = default)
        where TState : allows ref struct
    {
        ArgumentNullException.ThrowIfNull(action);
        using var session = BeginDrawing(cancellationToken);
        action(new DrawingCanvas(session), state);
    }

    private DrawingSession BeginDrawing(CancellationToken cancellationToken)
    {
        Owner.EnsureCanModify("draw on the image");
        cancellationToken.ThrowIfCancellationRequested();
        var frames = GetAllFrames();
        var storages = new PixelStorage[frames.Length];
        for (var i = 0; i < frames.Length; i++)
        {
            storages[i] = frames[i].GetStorage();
        }

        return DrawingSession.Begin(this, frames, storages, cancellationToken);
    }
}
