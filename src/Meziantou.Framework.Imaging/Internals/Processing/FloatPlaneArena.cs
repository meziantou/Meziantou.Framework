namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Rents the planes of a metric and releases them: each one as soon as it is no longer needed, and all the remaining ones
/// on disposal, including when a later rental exceeds the allocation limit.
/// </summary>
internal sealed class FloatPlaneArena : IDisposable
{
    private readonly AllocationScope _scope;
    private readonly List<FloatPlane> _planes = [];

    public FloatPlaneArena(AllocationScope scope)
    {
        _scope = scope;
    }

    /// <exception cref="ImageResourceLimitException">The plane exceeds the allocation limit.</exception>
    public FloatPlane Rent(int width, int height)
    {
        var plane = new FloatPlane(_scope, width, height);
        _planes.Add(plane);
        return plane;
    }

    /// <exception cref="ImageResourceLimitException">The planes exceed the allocation limit.</exception>
    public FloatPlane[] Rent(int count, int width, int height)
    {
        var planes = new FloatPlane[count];
        for (var i = 0; i < count; i++)
        {
            planes[i] = Rent(width, height);
        }

        return planes;
    }

    public void Release(FloatPlane? plane)
    {
        if (plane is not null && _planes.Remove(plane))
        {
            plane.Dispose();
        }
    }

    public void Release(FloatPlane?[] planes)
    {
        foreach (var plane in planes)
        {
            Release(plane);
        }
    }

    public void Dispose()
    {
        foreach (var plane in _planes)
        {
            plane.Dispose();
        }

        _planes.Clear();
    }
}
