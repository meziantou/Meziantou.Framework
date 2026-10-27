namespace Meziantou.Framework.Imaging.Internals;

/// <summary>How the sums of the kernels of a <see cref="ConvolutionPlan"/> make an output working sample.</summary>
internal enum KernelCombination
{
    /// <summary>The sum of the only kernel.</summary>
    None,

    /// <summary>The magnitude <c>sqrt(a^2 + b^2)</c> of the sums of two kernels (two gradients).</summary>
    Magnitude,

    /// <summary>The largest sum (a compass operator).</summary>
    Maximum,

    /// <summary>The largest absolute value of the sums (a compass whose matrices come in opposite pairs, or the absolute value of one sum).</summary>
    MaximumAbsolute,
}
