using System.Globalization;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Barcodes.Internal.RMQR;

/// <summary>The sizes of rMQR symbols allowed by <see cref="RMQRCodeOptions"/>, in modules.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct RMQRSizeConstraints(int? Height, int? Width, int? MaximumHeight, int? MaximumWidth)
{
    public bool IsEmpty => Height is null && Width is null && MaximumHeight is null && MaximumWidth is null;

    public bool Allows(int height, int width)
    {
        return (Height is null || Height == height)
            && (Width is null || Width == width)
            && (MaximumHeight is null || height <= MaximumHeight)
            && (MaximumWidth is null || width <= MaximumWidth);
    }

    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Height is { } height)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"height {height}"));

        if (Width is { } width)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"width {width}"));

        if (MaximumHeight is { } maximumHeight)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"height at most {maximumHeight}"));

        if (MaximumWidth is { } maximumWidth)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"width at most {maximumWidth}"));

        return string.Join(", ", parts);
    }
}
