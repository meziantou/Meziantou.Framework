namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>Identifies what <see cref="VectorPath.Combine"/> keeps of the areas two paths fill.</summary>
public enum PathCombineMode
{
    /// <summary>What at least one of the two paths fills.</summary>
    Union = 0,

    /// <summary>What both paths fill.</summary>
    Intersect = 1,

    /// <summary>What the first path fills and the second one does not.</summary>
    Exclude = 2,

    /// <summary>What one of the two paths fills, and not the other.</summary>
    Xor = 3,
}
