namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A sample type of a color conversion: how stored samples map to normalized values in [0, 1] and back.</summary>
internal interface IIccSample<T>
    where T : unmanaged
{
    /// <summary>Normalizes a stored sample.</summary>
    static abstract double Load(T value);

    /// <summary>Stores a normalized value: clipped to [0, 1], then rounded to nearest (ties upward) for integer samples.</summary>
    static abstract T Store(double value);
}
