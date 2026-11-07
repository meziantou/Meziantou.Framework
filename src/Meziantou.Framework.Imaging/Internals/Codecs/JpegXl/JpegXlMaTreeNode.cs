using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A node of a meta-adaptive tree: a decision on a property (the left child is taken when the property is greater than
/// the value), or a leaf that gives the context, the predictor, the offset and the multiplier of a sample.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct JpegXlMaTreeNode
{
    /// <summary>The property a decision node tests, or -1 for a leaf.</summary>
    public int Property;

    /// <summary>The value the property is compared with, or the offset added to the prediction of a leaf.</summary>
    public int ValueOrOffset;

    /// <summary>The index of the child taken when the property is greater than the value, or the context of a leaf.</summary>
    public int LeftOrContext;

    /// <summary>The index of the other child, or the predictor of a leaf.</summary>
    public int RightOrPredictor;

    /// <summary>The multiplier of the decoded value of a leaf.</summary>
    public int Multiplier;

    public readonly bool IsLeaf => Property < 0;
}
