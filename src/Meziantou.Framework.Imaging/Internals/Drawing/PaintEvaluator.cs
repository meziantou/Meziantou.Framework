namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Computes the colors a brush that is not a single color paints on the pixels of a row, in the pixel format of the frame.</summary>
internal abstract class PaintEvaluator
{
    /// <summary>Gets a value indicating whether the paint has no opacity anywhere.</summary>
    public virtual bool IsTransparent => false;

    /// <summary>Starts the painting of a shape: what the paint reads is held until <see cref="End"/>.</summary>
    /// <exception cref="InvalidOperationException">What the paint reads is being changed.</exception>
    public virtual void Begin()
    {
    }

    /// <summary>Ends the painting of a shape.</summary>
    public virtual void End()
    {
    }

    /// <summary>Computes the colors of a run of pixels: four samples per pixel, laid out as <see cref="DrawingColor"/>.</summary>
    public abstract ReadOnlySpan<ushort> Evaluate(int y, int x, int count);

    /// <summary>Computes floating-point paint samples when they cannot be represented by normalized integer colors.</summary>
    public virtual bool TryEvaluateFloat(int y, int x, int count, double alphaFactor, out ReadOnlySpan<double> colors)
    {
        colors = default;
        return false;
    }

    /// <summary>Gets the color of every pixel of a row, when <see cref="Evaluate"/> gives them all the same color.</summary>
    /// <returns><see langword="false"/> when the pixels of the row may have different colors.</returns>
    public virtual bool TryGetRowColor(int y, int x, out DrawingColor color)
    {
        color = default;
        return false;
    }

    /// <summary>
    /// Creates an evaluator for another thread: it gives the same colors as this one, which was prepared and begun, with
    /// scratch of its own. What both read is shared and must not change until the worker is released.
    /// </summary>
    /// <returns>The worker, or <see langword="null"/> when the paint is evaluated on the calling thread only.</returns>
    /// <exception cref="ImageResourceLimitException">The scratch of the worker exceeds the allocation limit.</exception>
    public virtual PaintEvaluator? CreateWorker() => null;

    /// <summary>Releases the scratch of an evaluator that <see cref="CreateWorker"/> created; what it shares stays.</summary>
    public virtual void ReleaseWorker()
    {
    }

    /// <summary>Rents a buffer of the length of another one, for the scratch of a worker.</summary>
    /// <exception cref="ImageResourceLimitException">The buffer exceeds the allocation limit.</exception>
    private protected static PooledBuffer? RentLike(AllocationScope scope, PooledBuffer? buffer) => buffer is null ? null : scope.Rent(buffer.Length, AllocationKind.Temporary, clear: true);
}
