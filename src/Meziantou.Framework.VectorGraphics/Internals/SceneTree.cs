namespace Meziantou.Framework.VectorGraphics;

/// <summary>Walks the elements of a scene.</summary>
internal static class SceneTree
{
    /// <summary>
    /// Enumerates an element and every element it draws: the children of the groups, the contents of the masks, of the
    /// patterns and of the markers, then the contents of the uses that are not drawn otherwise. An element that is
    /// reached several times is enumerated each time; a pattern, a marker and the content of a use once.
    /// </summary>
    public static IEnumerable<SceneElement> EnumerateElements(SceneElement root) => Walk(root).Elements;

    /// <summary>
    /// Walks a scene: <c>Elements</c> as <see cref="EnumerateElements"/> gives them, and <c>Detached</c>, the contents of
    /// the uses that the scene does not draw otherwise, each once: a content inside another one is written with it, so it
    /// is not detached itself.
    /// </summary>
    public static (List<SceneElement> Elements, List<SceneElement> Detached) Walk(SceneElement root)
    {
        var elements = new List<SceneElement>();
        var seen = new HashSet<SceneElement>(ReferenceEqualityComparer.Instance);
        var uses = new List<SceneElement>();
        var shared = new HashSet<object>(ReferenceEqualityComparer.Instance);
        Visit(root);

        // The contents of the uses the scene does not draw, then the contents of their own uses, in the order they are found
        var candidates = new List<SceneElement>();
        var candidateSet = new HashSet<SceneElement>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < uses.Count; i++)
        {
            var content = uses[i];
            if (seen.Contains(content) || !candidateSet.Add(content))
                continue;

            candidates.Add(content);
            foreach (var descendant in GetDescendants(content))
            {
                if (descendant is SceneUse use)
                {
                    uses.Add(use.Content);
                }
            }
        }

        // The roots are the contents that are not inside another one
        var nested = new HashSet<SceneElement>(ReferenceEqualityComparer.Instance);
        foreach (var candidate in candidates)
        {
            foreach (var descendant in GetDescendants(candidate))
            {
                if (!ReferenceEquals(descendant, candidate) && candidateSet.Contains(descendant))
                {
                    nested.Add(descendant);
                }
            }
        }

        var detached = candidates.Where(candidate => !nested.Contains(candidate)).ToList();
        foreach (var element in detached)
        {
            Visit(element);
        }

        return (elements, detached);

        void Visit(SceneElement start)
        {
            var stack = new Stack<SceneElement>();
            stack.Push(start);
            while (stack.TryPop(out var element))
            {
                elements.Add(element);
                seen.Add(element);
                if (element is SceneUse use)
                {
                    uses.Add(use.Content);
                }

                var contents = GetContents(element, shared);
                for (var i = contents.Count - 1; i >= 0; i--)
                {
                    stack.Push(contents[i]);
                }
            }
        }
    }

    /// <summary>Enumerates an element and the elements it draws (<see cref="GetContents(SceneElement)"/>), each once, without following the uses.</summary>
    public static IEnumerable<SceneElement> GetDescendants(SceneElement root)
    {
        var visited = new HashSet<SceneElement>(ReferenceEqualityComparer.Instance);
        var shared = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<SceneElement>();
        stack.Push(root);
        while (stack.TryPop(out var element))
        {
            if (!visited.Add(element))
                continue;

            yield return element;
            foreach (var content in GetContents(element, shared))
            {
                stack.Push(content);
            }
        }
    }

    /// <summary>Gets the elements an element draws, in order: its mask, the patterns of its paints, its markers, its children; not the content of a use.</summary>
    public static List<SceneElement> GetContents(SceneElement element) => GetContents(element, new HashSet<object>(ReferenceEqualityComparer.Instance));

    // The elements an element draws, in order: its mask, the patterns of its paints, its markers, its children
    private static List<SceneElement> GetContents(SceneElement element, HashSet<object> patterns)
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

        switch (element)
        {
            case ScenePath path:
                AddMarker(path.MarkerStart);
                AddMarker(path.MarkerMid);
                AddMarker(path.MarkerEnd);
                break;

            case SceneLine line:
                AddMarker(line.MarkerStart);
                AddMarker(line.MarkerEnd);
                break;
        }

        if (element is SceneGroup group)
        {
            contents.AddRange(group.Children);
        }

        return contents;

        void AddMarker(SceneMarker? marker)
        {
            if (marker is not null && patterns.Add(marker))
            {
                contents.Add(marker.Content);
            }
        }

        void AddPattern(ScenePaint? paint)
        {
            if (paint is PatternPaint pattern && patterns.Add(pattern))
            {
                contents.Add(pattern.Content);
            }
        }
    }
}
