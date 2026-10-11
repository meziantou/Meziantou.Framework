namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// One step of an ICC color conversion, applied in place to the components of one color. Stages are immutable and
/// evaluated in <see cref="double"/>: they are the numerical reference of the conversion.
/// </summary>
internal abstract class IccStage
{
    /// <summary>Applies the stage.</summary>
    /// <param name="values">The components of the color; at least <see cref="IccPipeline.MaxChannels"/> elements.</param>
    public abstract void Apply(Span<double> values);
}
