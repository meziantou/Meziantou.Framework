namespace Meziantou.Framework.Imaging.Internals;

/// <summary>What the samples an opcode list modifies are.</summary>
internal enum DngSampleValues
{
    /// <summary>The stored integer values of list 1, divided by 65,535; they are rounded back after each opcode.</summary>
    StoredIntegers,

    /// <summary>The stored floating-point values of list 1, which are not clipped.</summary>
    StoredFloats,

    /// <summary>The linear reference values of lists 2 and 3, clipped to [0, 1].</summary>
    Linear,
}
