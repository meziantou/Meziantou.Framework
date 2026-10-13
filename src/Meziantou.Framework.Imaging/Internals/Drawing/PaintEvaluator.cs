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
}
