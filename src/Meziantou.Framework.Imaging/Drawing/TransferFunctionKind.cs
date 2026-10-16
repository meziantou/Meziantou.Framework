namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>The kind of a <see cref="TransferFunction"/>: the <c>type</c> of a <c>feFunc*</c> element of SVG.</summary>
public enum TransferFunctionKind
{
    /// <summary>The value is unchanged.</summary>
    Identity = 0,

    /// <summary>The value is interpolated in a table of values spread evenly from 0 to 1.</summary>
    Table = 1,

    /// <summary>The value is the value of a table of steps of the same width.</summary>
    Discrete = 2,

    /// <summary>The value is multiplied by a slope, and an intercept is added.</summary>
    Linear = 3,

    /// <summary>The value is raised to a power, multiplied by an amplitude, and an offset is added.</summary>
    Gamma = 4,
}
