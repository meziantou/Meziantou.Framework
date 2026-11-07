using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The segmentation features of a frame (AV1 specification, section 5.9.14): <c>FeatureEnabled</c> and <c>FeatureData</c> for
/// each of the 8 segments and 8 features. A value type: loading the features of a reference frame is a copy.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal struct Av1SegmentationFeatures
{
    private InlineArray8<byte> _enabled;
    private InlineArray8<InlineArray8<short>> _data;

    /// <summary>Returns <c>FeatureEnabled[ segmentId ][ feature ]</c>.</summary>
    public readonly bool IsEnabled(int segmentId, int feature) => ((_enabled[segmentId] >> feature) & 1) is not 0;

    /// <summary>Returns <c>FeatureData[ segmentId ][ feature ]</c>.</summary>
    public readonly int GetData(int segmentId, int feature) => _data[segmentId][feature];

    public void Set(int segmentId, int feature, bool enabled, int data)
    {
        _enabled[segmentId] = (byte)(enabled ? _enabled[segmentId] | (1 << feature) : _enabled[segmentId] & ~(1 << feature));
        _data[segmentId][feature] = (short)data;
    }
}
