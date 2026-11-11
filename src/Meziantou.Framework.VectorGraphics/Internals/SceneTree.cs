namespace Meziantou.Framework.VectorGraphics;

/// <summary>Walks the elements of a scene.</summary>
internal static class SceneTree
{
    /// <summary>
    /// Enumerates an element and every element it draws: the children of the groups, the contents of the masks and of
    /// the patterns. An element that is reached several times is enumerated each time; a pattern once.
    /// </summary>
    public static IEnumerable<SceneElement> EnumerateElements(SceneElement root)
    {
        var patterns = new HashSet<PatternPaint>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<SceneElement>();
        stack.Push(root);
        while (stack.TryPop(out var element))
        {
            yield return element;
            var contents = GetContents(element, patterns);
            for (var i = contents.Count - 1; i >= 0; i--)
            {
                stack.Push(contents[i]);
            }
        }
    }

    // The elements an element draws, in order: its mask, the patterns of its paints, its children
    private static List<SceneElement> GetContents(SceneElement element, HashSet<PatternPaint> patterns)
    {
        var contents = new List<SceneElement>();
        if (element.Mask is { } mask)
        {
            contents.Add(mask.Content);
        }

        if (element is SceneShape shape)
        {
            AddPattern(shape.Fill);
            AddPattern(shape.Stroke);
        }

        if (element is SceneGroup group)
        {
            contents.AddRange(group.Children);
        }

        return contents;

        void AddPattern(ScenePaint? paint)
        {
            if (paint is PatternPaint pattern && patterns.Add(pattern))
            {
                contents.Add(pattern.Content);
            }
        }
    }
}
