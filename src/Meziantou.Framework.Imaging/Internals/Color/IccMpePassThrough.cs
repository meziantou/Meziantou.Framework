using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The 'bACS' and 'eACS' elements: placeholders that pass their values through (ICC.2:2023 section 11.2.17).</summary>
internal sealed class IccMpePassThrough : IccMpeElement
{
    public IccMpePassThrough(int channels)
        : base(channels, channels)
    {
    }

    public override void Apply(ReadOnlySpan<double> input, Span<double> output, Span<double> scratch) => input.CopyTo(output);
}
