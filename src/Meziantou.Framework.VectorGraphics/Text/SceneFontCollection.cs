using System.Collections.Concurrent;

namespace Meziantou.Framework.VectorGraphics;

/// <summary>
/// The fonts of a scene: the faces of one or more families. The layout measures each text with them
/// (<see cref="FontFaceTextMeasurer(SceneFontCollection)"/>), and the renderers draw it with them (<see cref="Scene.Fonts"/>),
/// so each text is measured and drawn with the same font.
/// </summary>
/// <remarks>
/// <para>
/// The family of a text is <see cref="SceneFont.Family"/>, or the family of the scene when it is <see langword="null"/>. It is
/// a CSS <c>font-family</c> value: the first family of the list the collection has is used, and
/// <see cref="DefaultFamily"/> when it has none of them. Within the family, the face closest to the style of the text is
/// used: the same slant first, then the nearest weight (400 for regular text, 700 for bold text).
/// </para>
/// <para>
/// The characters the face of the text does not have are drawn with the other families of the collection, in the order
/// they were added: each grapheme cluster uses the first face that has all its characters.
/// </para>
/// <para>A collection is immutable and can be used by several threads at the same time.</para>
/// </remarks>
public sealed class SceneFontCollection
{
    private readonly ConcurrentDictionary<(string? Family, bool Bold, bool Italic), SceneFontFace[]> _stacks = new();

    /// <summary>Initializes a new instance of the <see cref="SceneFontCollection"/> class. The default family is the family of the first face.</summary>
    /// <param name="faces">The faces, in order of preference for the characters a family does not have.</param>
    /// <exception cref="ArgumentException"><paramref name="faces"/> is empty or contains <see langword="null"/>.</exception>
    public SceneFontCollection(params IEnumerable<SceneFontFace> faces)
        : this(faces, defaultFamily: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="SceneFontCollection"/> class.</summary>
    /// <param name="faces">The faces, in order of preference for the characters a family does not have.</param>
    /// <param name="defaultFamily">The family of the texts that name no family of the collection, or <see langword="null"/> for the family of the first face.</param>
    /// <exception cref="ArgumentException"><paramref name="faces"/> is empty or contains <see langword="null"/>, or no face is of <paramref name="defaultFamily"/>.</exception>
    public SceneFontCollection(IEnumerable<SceneFontFace> faces, string? defaultFamily)
    {
        ArgumentNullException.ThrowIfNull(faces);
        SceneFontFace[] array = [.. faces];
        if (array.Length is 0)
            throw new ArgumentException("A font collection needs at least one face.", nameof(faces));

        if (Array.IndexOf(array, null) >= 0)
            throw new ArgumentException("The faces cannot contain null.", nameof(faces));

        Faces = array;
        Families = [.. array.Select(face => face.FamilyName).Distinct(StringComparer.OrdinalIgnoreCase)];
        if (defaultFamily is null)
        {
            DefaultFamily = array[0].FamilyName;
        }
        else
        {
            DefaultFamily = Families.FirstOrDefault(family => string.Equals(family, defaultFamily, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"The collection has no face of the family '{defaultFamily}'.", nameof(defaultFamily));
        }
    }

    /// <summary>Gets the faces, in the order they were added.</summary>
    public IReadOnlyList<SceneFontFace> Faces { get; }

    /// <summary>Gets the families of the faces, in the order they were added.</summary>
    public IReadOnlyList<string> Families { get; }

    /// <summary>Gets the family of the texts that name no family of the collection.</summary>
    public string DefaultFamily { get; }

    /// <summary>Gets the families as a CSS <c>font-family</c> value: <see cref="DefaultFamily"/>, then the other families, quoted when needed.</summary>
    /// <remarks>The measurers that use the collection have this family (<see cref="ITextMeasurer.FontFamily"/>), so it is the family of their scenes.</remarks>
    public string FontFamily => CssFontFamily.Format(Families.Where(family => !string.Equals(family, DefaultFamily, StringComparison.OrdinalIgnoreCase)).Prepend(DefaultFamily));

    /// <summary>Loads the faces of font files into a collection. The default family is the family of the first face.</summary>
    /// <param name="paths">The paths of the files.</param>
    /// <returns>The collection.</returns>
    /// <exception cref="ArgumentException"><paramref name="paths"/> is empty.</exception>
    /// <exception cref="Fonts.FontException">A file is not a font, or a font is malformed, uses a feature that is not supported, or exceeds a limit.</exception>
    /// <exception cref="IOException">An I/O error occurred.</exception>
    public static SceneFontCollection Load(params IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return new SceneFontCollection(paths.SelectMany(SceneFontFace.Load));
    }

    /// <summary>Indicates whether the collection has a face of a family.</summary>
    /// <param name="family">The name of the family, compared ignoring the case of ASCII letters.</param>
    /// <returns><see langword="true"/> when a face is of the family.</returns>
    public bool ContainsFamily(string family)
    {
        ArgumentNullException.ThrowIfNull(family);
        return Families.Contains(family, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Gets the face that draws a text.</summary>
    /// <param name="font">The font of the text.</param>
    /// <param name="defaultFamily">The family of the text when <see cref="SceneFont.Family"/> is <see langword="null"/>, usually <see cref="Scene.FontFamily"/>, or <see langword="null"/> for <see cref="DefaultFamily"/>.</param>
    /// <returns>The face.</returns>
    public SceneFontFace GetFace(SceneFont font, string? defaultFamily = null) => GetStack(font, defaultFamily)[0];

    /// <summary>Gets the faces that draw a text: the face of its family and style, then the faces of the other families, closest to the style, for the characters it does not have.</summary>
    /// <param name="font">The font of the text.</param>
    /// <param name="defaultFamily">The family of the text when <see cref="SceneFont.Family"/> is <see langword="null"/>, usually <see cref="Scene.FontFamily"/>, or <see langword="null"/> for <see cref="DefaultFamily"/>.</param>
    /// <returns>The faces, by order of preference.</returns>
    public IReadOnlyList<SceneFontFace> GetFaces(SceneFont font, string? defaultFamily = null) => GetStack(font, defaultFamily);

    /// <summary>Splits a text into the runs that one face draws: each grapheme cluster uses the first face of the stack that has all its characters.</summary>
    internal static List<(Range Range, SceneFontFace Face)> SplitRuns(string text, IReadOnlyList<SceneFontFace> stack)
    {
        var runs = new List<(Range Range, SceneFontFace Face)>();
        if (text.Length is 0)
            return runs;

        if (stack.Count is 1)
        {
            runs.Add((0..text.Length, stack[0]));
            return runs;
        }

        var start = 0;
        SceneFontFace? current = null;
        foreach (var cluster in Unicode.EnumerateGraphemeClusters(text))
        {
            var face = SelectFace(stack, text.AsSpan(cluster));
            var (offset, _) = cluster.GetOffsetAndLength(text.Length);
            if (current is not null && face != current)
            {
                runs.Add((start..offset, current));
                start = offset;
            }

            current = face;
        }

        runs.Add((start..text.Length, current!));
        return runs;
    }

    private SceneFontFace[] GetStack(SceneFont font, string? defaultFamily)
    {
        return _stacks.GetOrAdd((font.Family ?? defaultFamily, font.Bold, font.Italic), static (key, collection) => collection.CreateStack(key.Family, key.Bold, key.Italic), this);
    }

    private SceneFontFace[] CreateStack(string? familyList, bool bold, bool italic)
    {
        var family = DefaultFamily;
        if (familyList is not null)
        {
            foreach (var name in CssFontFamily.Parse(familyList))
            {
                if (Families.FirstOrDefault(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase)) is { } match)
                {
                    family = match;
                    break;
                }
            }
        }

        var weight = bold ? 700 : 400;
        var stack = new List<SceneFontFace> { GetClosestFace(family, weight, italic) };
        foreach (var other in Families)
        {
            if (!string.Equals(other, family, StringComparison.OrdinalIgnoreCase))
            {
                stack.Add(GetClosestFace(other, weight, italic));
            }
        }

        return [.. stack];
    }

    private SceneFontFace GetClosestFace(string family, int weight, bool italic)
    {
        SceneFontFace? best = null;
        var bestScore = int.MaxValue;
        foreach (var face in Faces)
        {
            if (!string.Equals(face.FamilyName, family, StringComparison.OrdinalIgnoreCase))
                continue;

            var score = (face.IsItalic == italic ? 0 : 10_000) + Math.Abs(face.Weight - weight);
            if (score < bestScore)
            {
                best = face;
                bestScore = score;
            }
        }

        return best!;
    }

    private static SceneFontFace SelectFace(IReadOnlyList<SceneFontFace> stack, ReadOnlySpan<char> cluster)
    {
        var withFirst = -1;
        for (var i = 0; i < stack.Count; i++)
        {
            if (HasCluster(stack[i], cluster, out var hasFirst))
                return stack[i];

            if (hasFirst && withFirst < 0)
            {
                withFirst = i;
            }
        }

        return stack[Math.Max(withFirst, 0)];
    }

    // A face has a cluster when it has a glyph for each of its characters that needs one, alone or composed with the one before
    private static bool HasCluster(SceneFontFace face, ReadOnlySpan<char> cluster, out bool hasFirst)
    {
        hasFirst = false;
        var hasAll = true;
        var previous = -1;
        var isFirst = true;
        foreach (var rune in cluster.EnumerateRunes())
        {
            var codePoint = rune.Value;
            if (codePoint is < 0x20 or (>= 0x7F and <= 0x9F) || UnicodeProperties.IsDefaultIgnorable(codePoint))
                continue;

            var hasGlyph = face.Face.MapCodePoint(codePoint) != 0
                || (previous >= 0 && UnicodeNormalization.TryComposeCanonical(previous, codePoint, out var composite) && face.Face.MapCodePoint(composite) != 0);
            hasFirst |= isFirst && hasGlyph;
            hasAll &= hasGlyph;
            isFirst = false;
            previous = codePoint;
        }

        return hasAll;
    }
}
