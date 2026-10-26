namespace Meziantou.Framework.Imaging.Internals;

/// <summary>What an operation requires of the floating-point samples it reads (<see cref="FloatingPointSampleRange"/>).</summary>
internal enum FloatingPointSampleRequirement
{
    /// <summary>The colors are numbers in [0, 1]; alpha can be anything.</summary>
    UnitColors,

    /// <summary>Every sample, alpha included, is a number in [0, 1].</summary>
    UnitSamples,

    /// <summary>Every sample, alpha included, is a finite number.</summary>
    Finite,
}
