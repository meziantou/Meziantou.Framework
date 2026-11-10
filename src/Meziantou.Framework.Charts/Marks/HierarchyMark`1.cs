namespace Meziantou.Framework.Charts;

/// <summary>A mark that draws the nodes of a tree in the plot area, without position scales. The nodes are colored by the color scale.</summary>
/// <typeparam name="T">The type of the data of the nodes.</typeparam>
public abstract class HierarchyMark<T> : Mark
{
    private protected HierarchyMark(HierarchyNode<T> root)
    {
        ArgumentNullException.ThrowIfNull(root);
        Root = root;
    }

    /// <summary>Gets the root of the tree. Its values must be computed (<see cref="HierarchyNode{T}.Sum"/>).</summary>
    public HierarchyNode<T> Root { get; }

    /// <summary>Gets or sets the channel of the fill color of a node, or <see langword="null"/> for the name of its ancestor at depth 1 (its top-level group).</summary>
    public Func<HierarchyNode<T>, object?>? Fill { get; set; }

    /// <summary>Gets or sets a single fill color for every node, used when <see cref="Fill"/> is not set.</summary>
    public SceneColor? FillColor { get; set; }

    /// <summary>Gets or sets the label of a node, or <see langword="null"/> for its name. A label is drawn only when it fits.</summary>
    public Func<HierarchyNode<T>, string?>? Label { get; set; }

    /// <summary>Gets or sets the tooltip of a node, or <see langword="null"/> for its path and its value.</summary>
    public Func<HierarchyNode<T>, string?>? Title { get; set; }

    /// <summary>
    /// Gets or sets the <c>data-*</c> attributes of the shape of a node in the SVG output, or <see langword="null"/> for
    /// none. See <see cref="Mark{T}.DataAttributes"/> for the names and the values.
    /// </summary>
    public Func<HierarchyNode<T>, IEnumerable<KeyValuePair<string, object?>>?>? DataAttributes { get; set; }

    internal override MarkTraits GetTraits(MarkData data) => MarkTraits.Frame | MarkTraits.HideXAxis | MarkTraits.HideYAxis;

    internal sealed override MarkData Prepare()
    {
        var nodes = Root.Descendants().ToList();
        var data = new MarkData(nodes.Count)
        {
            State = nodes,
            Text = [.. nodes.Select(node => Label is null ? node.Name : Label(node))],
            Titles = [.. nodes.Select(node => Title is null ? GetDefaultTitle(node) : Title(node))],
            DataAttributes = DataAttributes is null ? null : [.. nodes.Select(node => DataAttributeValues.Evaluate(DataAttributes(node)))],
        };

        if (Fill is not null || FillColor is null)
        {
            data.Fill = ChannelValues.FromObjects([.. nodes.Select(node => Fill is null ? GetGroup(node) : Fill(node))]);
        }

        return data;
    }

    internal sealed override void Render(RenderContext context, MarkData data, SceneGroup group)
    {
        var nodes = (List<HierarchyNode<T>>)data.State!;
        var indexes = new Dictionary<HierarchyNode<T>, int>(nodes.Count);
        for (var i = 0; i < nodes.Count; i++)
        {
            indexes[nodes[i]] = i;
        }

        Render(context, data, group, indexes);
    }

    private protected abstract void Render(RenderContext context, MarkData data, SceneGroup group, Dictionary<HierarchyNode<T>, int> indexes);

    private protected SceneColor GetFill(RenderContext context, MarkData data, int index) => context.GetColor(data.Fill, index, FillColor);

    private protected static T2 Decorate<T2>(T2 element, MarkData data, int index)
        where T2 : SceneElement
    {
        element.Tooltip = data.Titles?[index];
        DataAttributeValues.Apply(element, data.DataAttributes, index);
        return element;
    }

    private static string? GetGroup(HierarchyNode<T> node)
    {
        if (node.Depth is 0)
            return null;

        var group = node;
        while (group.Depth > 1)
        {
            group = group.Parent!;
        }

        return group.Name;
    }

    private static string GetDefaultTitle(HierarchyNode<T> node)
    {
        var path = string.Join(" / ", node.Ancestors().Reverse().Where(ancestor => ancestor.Name.Length > 0).Select(ancestor => ancestor.Name));
        return path + ": " + NumberFormats.Default(node.Value);
    }
}
