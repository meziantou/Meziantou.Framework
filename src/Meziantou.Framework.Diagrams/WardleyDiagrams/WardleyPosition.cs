using System.Runtime.InteropServices;

namespace Meziantou.Framework.Diagrams;

/// <summary>A position on a Wardley map: its visibility, from 0 at the bottom to 1 at the top, and its evolution, from 0 on the left to 1 on the right.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct WardleyPosition
{
    /// <summary>Initializes a new instance of the <see cref="WardleyPosition"/> struct.</summary>
    /// <param name="visibility">The visibility, between 0 and 1.</param>
    /// <param name="evolution">The evolution, between 0 and 1.</param>
    public WardleyPosition(double visibility, double evolution)
    {
        Visibility = WardleyCoordinates.Check(visibility, nameof(visibility));
        Evolution = WardleyCoordinates.Check(evolution, nameof(evolution));
    }

    /// <summary>Gets the visibility, between 0 (bottom) and 1 (top).</summary>
    public double Visibility { get; }

    /// <summary>Gets the evolution, between 0 (left) and 1 (right).</summary>
    public double Evolution { get; }
}
