namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The annotation of a ruby, laid out on its own, and the room its base takes in the line.</summary>
internal sealed class RubyAnnotation
{
    /// <summary>Gets the annotation laid out in the writing mode of the layout, or <see langword="null"/> for an empty annotation.</summary>
    public required TextLayoutData? Annotation { get; init; }

    /// <summary>Gets the length of the annotation along the line.</summary>
    public required double Along { get; init; }

    /// <summary>Gets the size of the annotation across the line.</summary>
    public required double Extent { get; init; }

    /// <summary>Gets the room added before and after the base when it is shorter than its annotation.</summary>
    public required double Padding { get; init; }

    /// <summary>Gets the distance from the baseline of the base to the side the annotation stands on: the ascent of its font, or half its size around a central baseline.</summary>
    public required double BaseAbove { get; init; }
}
