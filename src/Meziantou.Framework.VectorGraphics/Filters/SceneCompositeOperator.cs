namespace Meziantou.Framework.VectorGraphics;

/// <summary>How a <see cref="SceneCompositePrimitive"/> combines its inputs <c>A</c> (<c>in</c>) and <c>B</c> (<c>in2</c>): the <c>operator</c> of <c>feComposite</c>.</summary>
public enum SceneCompositeOperator
{
    /// <summary><c>A</c> over <c>B</c> (<c>over</c>).</summary>
    Over,

    /// <summary>The part of <c>A</c> inside <c>B</c> (<c>in</c>).</summary>
    In,

    /// <summary>The part of <c>A</c> outside <c>B</c> (<c>out</c>).</summary>
    Out,

    /// <summary>The part of <c>A</c> inside <c>B</c>, over <c>B</c> (<c>atop</c>).</summary>
    Atop,

    /// <summary>The parts of <c>A</c> and <c>B</c> outside each other (<c>xor</c>).</summary>
    Xor,

    /// <summary>The sum of <c>A</c> and <c>B</c> (<c>lighter</c>).</summary>
    Lighter,

    /// <summary><c>k1 A B + k2 A + k3 B + k4</c>, on each component (<c>arithmetic</c>).</summary>
    Arithmetic,
}
