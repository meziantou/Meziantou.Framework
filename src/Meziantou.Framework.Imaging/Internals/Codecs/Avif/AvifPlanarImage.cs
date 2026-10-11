namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The decoded planes of an AVIF image, at their output size: the planes of a decoded AV1 frame, or planes assembled from the
/// tiles of a grid or from a frame with film grain.
/// </summary>
internal sealed class AvifPlanarImage : IDisposable
{
    private readonly Av1Plane?[] _planes = new Av1Plane?[3];
    private Av1FrameBuffer? _frame;

    private AvifPlanarImage(Av1SequenceHeader sequence, int width, int height)
    {
        Sequence = sequence;
        Width = width;
        Height = height;
    }

    /// <summary>Gets the sequence header of the coded image: its sample format and its color description.</summary>
    public Av1SequenceHeader Sequence { get; }

    public int Width { get; }

    public int Height { get; }

    public int PlaneCount => Sequence.PlaneCount;

    /// <summary>Wraps the planes of a decoded frame, which is released with the image.</summary>
    public static AvifPlanarImage FromFrame(Av1FrameBuffer frame)
    {
        var plane = frame.GetPlane(0);
        var image = new AvifPlanarImage(frame.Sequence, plane.Width, plane.Height) { _frame = frame };
        frame.AddReference();
        return image;
    }

    /// <summary>Rents the planes of an image of <paramref name="width"/> x <paramref name="height"/> luma samples.</summary>
    /// <exception cref="ImageResourceLimitException">The planes exceed the live-allocation limit.</exception>
    public static AvifPlanarImage Create(AllocationScope scope, Av1SequenceHeader sequence, int width, int height)
    {
        var image = new AvifPlanarImage(sequence, width, height);
        try
        {
            for (var plane = 0; plane < sequence.PlaneCount; plane++)
            {
                var subX = plane > 0 ? sequence.SubsamplingX : 0;
                var subY = plane > 0 ? sequence.SubsamplingY : 0;
                image._planes[plane] = Av1Plane.Create(scope, (width + subX) >> subX, (height + subY) >> subY, alignment: 1);
            }
        }
        catch
        {
            image.Dispose();
            throw;
        }

        return image;
    }

    public Av1Plane GetPlane(int plane) => _frame?.GetPlane(plane) ?? _planes[plane] ?? throw new ObjectDisposedException(nameof(AvifPlanarImage));

    public void Dispose()
    {
        _frame?.Release();
        _frame = null;
        for (var i = 0; i < _planes.Length; i++)
        {
            _planes[i]?.Dispose();
            _planes[i] = null;
        }
    }
}
