using System.Runtime.InteropServices;

namespace Meziantou.Framework.VectorGraphics;

/// <summary>What a primitive of a <see cref="SceneFilterGraph"/> reads, as the SVG <c>in</c> and <c>in2</c> attributes.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct SceneFilterInput
{
    private SceneFilterInput(SceneFilterInputKind kind, int resultIndex)
    {
        Kind = kind;
        ResultIndex = resultIndex;
    }

    /// <summary>Gets the result of the primitive before, or the drawing of the element for the first primitive. This is the default value.</summary>
    public static SceneFilterInput Previous => default;

    /// <summary>Gets the drawing of the element.</summary>
    public static SceneFilterInput SourceGraphic => new(SceneFilterInputKind.SourceGraphic, 0);

    /// <summary>Gets the alpha of the drawing of the element.</summary>
    public static SceneFilterInput SourceAlpha => new(SceneFilterInputKind.SourceAlpha, 0);

    /// <summary>Gets what the input reads.</summary>
    public SceneFilterInputKind Kind { get; }

    /// <summary>Gets the index of the primitive whose result is read, when <see cref="Kind"/> is <see cref="SceneFilterInputKind.Result"/>; 0 otherwise.</summary>
    public int ResultIndex { get; }

    /// <summary>Gets the result of an earlier primitive of the graph.</summary>
    /// <param name="index">The index of the primitive in <see cref="SceneFilterGraph.Primitives"/>: it must be before the primitive that reads it.</param>
    /// <returns>The input.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative.</exception>
    public static SceneFilterInput Result(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return new(SceneFilterInputKind.Result, index);
    }
}
