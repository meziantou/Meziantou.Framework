namespace Meziantou.Framework.Fonts;

/// <summary>
/// What the font code gets from the library that uses it: the limits that bound its work, and the storage of its
/// temporary buffers.
/// </summary>
/// <remarks>A font keeps the environment it was loaded with (<see cref="FontData.Environment"/>).</remarks>
public abstract class FontEnvironment
{
    /// <summary>Gets the limits.</summary>
    public abstract FontResourceLimits Limits { get; }

    /// <summary>Creates the allocator of an operation: the temporary buffers of the operation are rented from it.</summary>
    /// <param name="name">The name of the operation, for diagnostics.</param>
    public abstract FontAllocator CreateAllocator(string name);
}
