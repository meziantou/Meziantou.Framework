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

    /// <summary>
    /// Applies the stage to colors stored by component, with the same arithmetic as <see cref="Apply"/> for each color:
    /// the stages that matrix-based conversions use override it with loops over each component, the others apply
    /// <see cref="Apply"/> to each color.
    /// </summary>
    /// <param name="components">
    /// <see cref="IccPipeline.MaxChannels"/> rows of <see cref="IccPipeline.BatchSize"/> values: component <c>c</c> of color
    /// <c>i</c> is at <c>c * BatchSize + i</c>.
    /// </param>
    /// <param name="count">The number of colors, at most <see cref="IccPipeline.BatchSize"/>.</param>
    public virtual void ApplyBatch(Span<double> components, int count)
    {
        Span<double> values = stackalloc double[IccPipeline.MaxChannels];
        for (var i = 0; i < count; i++)
        {
            for (var channel = 0; channel < IccPipeline.MaxChannels; channel++)
            {
                values[channel] = components[(channel * IccPipeline.BatchSize) + i];
            }

            Apply(values);
            for (var channel = 0; channel < IccPipeline.MaxChannels; channel++)
            {
                components[(channel * IccPipeline.BatchSize) + i] = values[channel];
            }
        }
    }

    /// <summary>Gets the first <paramref name="count"/> values of component <paramref name="channel"/> of a batch (see <see cref="ApplyBatch"/>).</summary>
    protected static Span<double> GetComponent(Span<double> components, int channel, int count) => components.Slice(channel * IccPipeline.BatchSize, count);
}
