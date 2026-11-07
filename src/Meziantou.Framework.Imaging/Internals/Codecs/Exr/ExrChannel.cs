namespace Meziantou.Framework.Imaging.Internals;

/// <summary>One entry of the <c>channels</c> attribute.</summary>
internal sealed record ExrChannel(string Name, ExrPixelType PixelType, bool PLinear, int XSampling, int YSampling)
{
    public int BytesPerSample => ExrFormat.GetBytesPerSample(PixelType);
}
