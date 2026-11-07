namespace Meziantou.Framework.Imaging.TestHarness.Exr;

/// <summary>A channel of a <c>chlist</c> attribute.</summary>
/// <param name="Name">The channel name.</param>
/// <param name="PixelType">The pixel type: 0 (UINT), 1 (HALF) or 2 (FLOAT).</param>
/// <param name="PLinear">The pLinear flag.</param>
/// <param name="XSampling">The horizontal sampling.</param>
/// <param name="YSampling">The vertical sampling.</param>
public sealed record ExrChannelDescription(string Name, int PixelType, bool PLinear = false, int XSampling = 1, int YSampling = 1)
{
    /// <summary>A UINT channel.</summary>
    public static ExrChannelDescription UnsignedInt(string name) => new(name, 0);

    /// <summary>A HALF channel.</summary>
    public static ExrChannelDescription Half(string name) => new(name, 1);

    /// <summary>A FLOAT channel.</summary>
    public static ExrChannelDescription FloatingPoint(string name) => new(name, 2);
}
