using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Css.TypedOm;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://drafts.css-houdini.org/css-layout-api-1/#enumdef-childdisplaytype</summary>
[IdlInterface("ChildDisplayType", "https://drafts.css-houdini.org/css-layout-api-1/#enumdef-childdisplaytype")]
public enum ChildDisplayType
{
    /// <summary>The display of the children of the layout API container is blockified.</summary>
    [IdlMember("block", "https://drafts.css-houdini.org/css-layout-api-1/#dom-childdisplaytype-block")]
    Block,

    /// <summary>The display of the children is not blockified: the inline-level children are in a root inline box.</summary>
    [IdlMember("normal", "https://drafts.css-houdini.org/css-layout-api-1/#dom-childdisplaytype-normal")]
    Normal,
}

/// <summary>https://drafts.css-houdini.org/css-layout-api-1/#enumdef-layoutsizingmode</summary>
[IdlInterface("LayoutSizingMode", "https://drafts.css-houdini.org/css-layout-api-1/#enumdef-layoutsizingmode")]
public enum LayoutSizingMode
{
    /// <summary>The layout API container is sized like a block container: its sizes are given to the layout.</summary>
    [IdlMember("block-like", "https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutsizingmode-block-like")]
    BlockLike,

    /// <summary>The layout computes the sizes of the layout API container, unless its formatting context forces them.</summary>
    [IdlMember("manual", "https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutsizingmode-manual")]
    Manual,
}

/// <summary>The options of a layout class (https://drafts.css-houdini.org/css-layout-api-1/#dictdef-layoutoptions): its static <c>layoutOptions</c> property.</summary>
[IdlInterface("LayoutOptions", "https://drafts.css-houdini.org/css-layout-api-1/#dictdef-layoutoptions")]
public sealed class LayoutOptions
{
    /// <summary>Gets whether the display of the children of the layout API container is blockified.</summary>
    [IdlMember("childDisplay", "https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutoptions-childdisplay")]
    public ChildDisplayType ChildDisplay { get; init; } = ChildDisplayType.Block;

    /// <summary>Gets how the layout API container is sized.</summary>
    [IdlMember("sizing", "https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutoptions-sizing")]
    public LayoutSizingMode Sizing { get; init; } = LayoutSizingMode.BlockLike;
}

/// <summary>https://drafts.css-houdini.org/css-layout-api-1/#enumdef-blockfragmentationtype</summary>
[IdlInterface("BlockFragmentationType", "https://drafts.css-houdini.org/css-layout-api-1/#enumdef-blockfragmentationtype")]
public enum BlockFragmentationType
{
    /// <summary>The layout is not fragmented.</summary>
    [IdlMember("none", "https://drafts.css-houdini.org/css-layout-api-1/#dom-blockfragmentationtype-none")]
    None,

    /// <summary>The layout is fragmented by pages.</summary>
    [IdlMember("page", "https://drafts.css-houdini.org/css-layout-api-1/#dom-blockfragmentationtype-page")]
    Page,

    /// <summary>The layout is fragmented by columns.</summary>
    [IdlMember("column", "https://drafts.css-houdini.org/css-layout-api-1/#dom-blockfragmentationtype-column")]
    Column,

    /// <summary>The layout is fragmented by regions.</summary>
    [IdlMember("region", "https://drafts.css-houdini.org/css-layout-api-1/#dom-blockfragmentationtype-region")]
    Region,
}

/// <summary>https://drafts.css-houdini.org/css-layout-api-1/#enumdef-breaktype</summary>
[IdlInterface("BreakType", "https://drafts.css-houdini.org/css-layout-api-1/#enumdef-breaktype")]
public enum BreakType
{
    /// <summary>No break.</summary>
    [IdlMember("none", "https://drafts.css-houdini.org/css-layout-api-1/#dom-breaktype-none")]
    None,

    /// <summary>A break between two lines.</summary>
    [IdlMember("line", "https://drafts.css-houdini.org/css-layout-api-1/#dom-breaktype-line")]
    Line,

    /// <summary>A column break.</summary>
    [IdlMember("column", "https://drafts.css-houdini.org/css-layout-api-1/#dom-breaktype-column")]
    Column,

    /// <summary>A page break.</summary>
    [IdlMember("page", "https://drafts.css-houdini.org/css-layout-api-1/#dom-breaktype-page")]
    Page,

    /// <summary>A region break.</summary>
    [IdlMember("region", "https://drafts.css-houdini.org/css-layout-api-1/#dom-breaktype-region")]
    Region,
}

/// <summary>
/// The constraints a layout gives to a child (https://drafts.css-houdini.org/css-layout-api-1/#dictdef-layoutconstraintsoptions),
/// in the writing mode of the layout API container. A member that is not set is null.
/// </summary>
[IdlInterface("LayoutConstraintsOptions", "https://drafts.css-houdini.org/css-layout-api-1/#dictdef-layoutconstraintsoptions")]
public sealed class LayoutConstraintsOptions
{
    /// <summary>Gets the available inline size.</summary>
    [IdlMember("availableInlineSize", "https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutconstraintsoptions-availableinlinesize")]
    public double? AvailableInlineSize { get; init; }

    /// <summary>Gets the available block size.</summary>
    [IdlMember("availableBlockSize", "https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutconstraintsoptions-availableblocksize")]
    public double? AvailableBlockSize { get; init; }

    /// <summary>Gets the border-box inline size the child must have.</summary>
    [IdlMember("fixedInlineSize", "https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutconstraintsoptions-fixedinlinesize")]
    public double? FixedInlineSize { get; init; }

    /// <summary>Gets the border-box block size the child must have.</summary>
    [IdlMember("fixedBlockSize", "https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutconstraintsoptions-fixedblocksize")]
    public double? FixedBlockSize { get; init; }

    /// <summary>Gets the size the percentages of the inline axis resolve against.</summary>
    [IdlMember("percentageInlineSize", "https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutconstraintsoptions-percentageinlinesize")]
    public double? PercentageInlineSize { get; init; }

    /// <summary>Gets the size the percentages of the block axis resolve against.</summary>
    [IdlMember("percentageBlockSize", "https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutconstraintsoptions-percentageblocksize")]
    public double? PercentageBlockSize { get; init; }

    /// <summary>Gets the offset where the child should fragment.</summary>
    [IdlMember("blockFragmentationOffset", "https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutconstraintsoptions-blockfragmentationoffset")]
    public double? BlockFragmentationOffset { get; init; }

    /// <summary>Gets the type of the fragmentation of the child.</summary>
    [IdlMember("blockFragmentationType", "https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutconstraintsoptions-blockfragmentationtype")]
    public BlockFragmentationType BlockFragmentationType { get; init; } = BlockFragmentationType.None;

    /// <summary>Gets the data given to the layout of the child when it is a layout API container (a value of the script engine, structured cloned).</summary>
    [IdlMember("data", "https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutconstraintsoptions-data")]
    public object? Data { get; init; }
}

/// <summary>
/// The constraints of the layout of a layout API container (https://drafts.css-houdini.org/css-layout-api-1/#layoutconstraints),
/// in its writing mode: the third argument of the <c>layout()</c> callback.
/// </summary>
[IdlInterface("LayoutConstraints", "https://drafts.css-houdini.org/css-layout-api-1/#layoutconstraints")]
public sealed class LayoutConstraints
{
    internal LayoutConstraints()
    {
    }

    /// <summary>Gets the available inline size.</summary>
    [IdlMember("availableInlineSize", "https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutconstraints-availableinlinesize")]
    public double AvailableInlineSize { get; internal init; }

    /// <summary>Gets the available block size.</summary>
    [IdlMember("availableBlockSize", "https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutconstraints-availableblocksize")]
    public double AvailableBlockSize { get; internal init; }

    /// <summary>Gets the border-box inline size of the box when it is known before the layout, else null.</summary>
    [IdlMember("fixedInlineSize", "https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutconstraints-fixedinlinesize")]
    public double? FixedInlineSize { get; internal init; }

    /// <summary>Gets the border-box block size of the box when it is known before the layout, else null.</summary>
    [IdlMember("fixedBlockSize", "https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutconstraints-fixedblocksize")]
    public double? FixedBlockSize { get; internal init; }

    /// <summary>Gets the size the percentages of the inline axis of the box resolve against.</summary>
    [IdlMember("percentageInlineSize", "https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutconstraints-percentageinlinesize")]
    public double PercentageInlineSize { get; internal init; }

    /// <summary>Gets the size the percentages of the block axis of the box resolve against.</summary>
    [IdlMember("percentageBlockSize", "https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutconstraints-percentageblocksize")]
    public double PercentageBlockSize { get; internal init; }

    /// <summary>Gets the offset where the box should fragment, or null: the layout API containers are not fragmented by their layout.</summary>
    [IdlMember("blockFragmentationOffset", "https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutconstraints-blockfragmentationoffset")]
    public double? BlockFragmentationOffset { get; internal init; }

    /// <summary>Gets the type of the fragmentation: always <see cref="BlockFragmentationType.None"/>.</summary>
    [IdlMember("blockFragmentationType", "https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutconstraints-blockfragmentationtype")]
    public BlockFragmentationType BlockFragmentationType { get; internal init; }

    /// <summary>Gets the data given by the layout of the parent layout API container (a value of the script engine), or null.</summary>
    [IdlMember("data", "https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutconstraints-data")]
    public object? Data { get; internal init; }
}

/// <summary>
/// The box model edges of a layout API container (https://drafts.css-houdini.org/css-layout-api-1/#layoutedges): the sums of
/// its borders, scrollbars and paddings, in its writing mode.
/// </summary>
[IdlInterface("LayoutEdges", "https://drafts.css-houdini.org/css-layout-api-1/#layoutedges")]
public sealed class LayoutEdges
{
    internal LayoutEdges(LogicalSides<double> edges)
    {
        InlineStart = edges.InlineStart;
        InlineEnd = edges.InlineEnd;
        BlockStart = edges.BlockStart;
        BlockEnd = edges.BlockEnd;
    }

    /// <summary>Gets the edge of the inline-start side.</summary>
    [IdlMember("inlineStart", "https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutedges-inlinestart")]
    public double InlineStart { get; }

    /// <summary>Gets the edge of the inline-end side.</summary>
    [IdlMember("inlineEnd", "https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutedges-inlineend")]
    public double InlineEnd { get; }

    /// <summary>Gets the edge of the block-start side.</summary>
    [IdlMember("blockStart", "https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutedges-blockstart")]
    public double BlockStart { get; }

    /// <summary>Gets the edge of the block-end side.</summary>
    [IdlMember("blockEnd", "https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutedges-blockend")]
    public double BlockEnd { get; }

    /// <summary>Gets the sum of the edges of the inline axis.</summary>
    [IdlMember("inline", "https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutedges-inline")]
    public double Inline => InlineStart + InlineEnd;

    /// <summary>Gets the sum of the edges of the block axis.</summary>
    [IdlMember("block", "https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutedges-block")]
    public double Block => BlockStart + BlockEnd;
}

/// <summary>
/// The border-box min-content and max-content contributions of a child of a layout API container
/// (https://drafts.css-houdini.org/css-layout-api-1/#intrinsicsizes), in the inline axis of the container.
/// </summary>
[IdlInterface("IntrinsicSizes", "https://drafts.css-houdini.org/css-layout-api-1/#intrinsicsizes")]
public sealed class LayoutIntrinsicSizes
{
    internal LayoutIntrinsicSizes(double minContentSize, double maxContentSize)
    {
        MinContentSize = minContentSize;
        MaxContentSize = maxContentSize;
    }

    /// <summary>Gets the border-box min-content contribution.</summary>
    [IdlMember("minContentSize", "https://drafts.css-houdini.org/css-layout-api-1/#dom-intrinsicsizes-mincontentsize")]
    public double MinContentSize { get; }

    /// <summary>Gets the border-box max-content contribution.</summary>
    [IdlMember("maxContentSize", "https://drafts.css-houdini.org/css-layout-api-1/#dom-intrinsicsizes-maxcontentsize")]
    public double MaxContentSize { get; }
}

/// <summary>
/// The value returned by the <c>intrinsicSizes()</c> callback of a layout class
/// (https://drafts.css-houdini.org/css-layout-api-1/#dictdef-intrinsicsizesresultoptions): the border-box intrinsic sizes of
/// the layout API container.
/// </summary>
[IdlInterface("IntrinsicSizesResultOptions", "https://drafts.css-houdini.org/css-layout-api-1/#dictdef-intrinsicsizesresultoptions")]
public sealed class IntrinsicSizesResultOptions
{
    /// <summary>Gets the border-box max-content size, or null when it is not set.</summary>
    [IdlMember("maxContentSize", "https://drafts.css-houdini.org/css-layout-api-1/#dom-intrinsicsizesresultoptions-maxcontentsize")]
    public double? MaxContentSize { get; init; }

    /// <summary>Gets the border-box min-content size, or null when it is not set.</summary>
    [IdlMember("minContentSize", "https://drafts.css-houdini.org/css-layout-api-1/#dom-intrinsicsizesresultoptions-mincontentsize")]
    public double? MinContentSize { get; init; }
}

/// <summary>
/// A child of a layout API container (https://drafts.css-houdini.org/css-layout-api-1/#layoutchild): an in-flow box, before
/// its layout. The layout callbacks get the children of the box, read their styles, ask for their intrinsic sizes and lay
/// them out.
/// </summary>
[IdlInterface("LayoutChild", "https://drafts.css-houdini.org/css-layout-api-1/#layoutchild")]
public sealed class LayoutChild
{
    private readonly LayoutWorklet _worklet;
    private readonly IReadOnlyList<string> _childInputProperties;
    private StylePropertyMapReadOnly? _styleMap;

    internal LayoutChild(LayoutWorklet worklet, ContainerBox box, IReadOnlyList<string> childInputProperties)
    {
        _worklet = worklet;
        _childInputProperties = childInputProperties;
        Box = box;
    }

    /// <summary>The box of the child (the [[box]] internal slot).</summary>
    internal ContainerBox Box { get; }

    /// <summary>The layout API context the child was last given to (the [[unique id]] internal slot): the child is only usable in it.</summary>
    internal LayoutApiContext? Context { get; set; }

    /// <summary>
    /// Gets the computed values of the <c>childInputProperties</c> of the layout class for the child
    /// (https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutchild-stylemap).
    /// </summary>
    [IdlMember("styleMap", "https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutchild-stylemap")]
    public StylePropertyMapReadOnly StyleMap => _styleMap ??= new PaintStylePropertyMap(Box.Style, _childInputProperties);

    /// <summary>
    /// Asks for the border-box min-content and max-content contributions of the child
    /// (https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutchild-intrinsicsizes): the task is completed when the
    /// layout runs its work queue.
    /// </summary>
    /// <returns>A task that fails with an <c>InvalidStateError</c> <see cref="DomException"/> when the child does not belong to the layout that is running.</returns>
    [IdlMember("intrinsicSizes", "https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutchild-intrinsicsizes")]
    public Task<LayoutIntrinsicSizes> IntrinsicSizes()
    {
        var context = _worklet.CurrentContext;
        if (context is null || Context != context)
            return Task.FromException<LayoutIntrinsicSizes>(new DomException("The LayoutChild does not belong to the layout that is running", "InvalidStateError"));

        var task = new LayoutApiWorkTask(this, constraints: null);
        context.WorkQueue.Add(task);
        return task.IntrinsicSizesCompletion.Task;
    }

    /// <summary>
    /// Lays out the child with the given constraints (https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutchild-layoutnextfragment):
    /// the task is completed with the fragment when the layout runs its work queue.
    /// </summary>
    /// <param name="constraints">The constraints, in the writing mode of the layout API container.</param>
    /// <param name="breakToken">The break token of the previous fragment of the child, or null for its first fragment.</param>
    /// <returns>
    /// A task that fails with a <see cref="DomException"/>: an <c>InvalidStateError</c> when the child or the break token does
    /// not belong to the layout that is running, a <c>NotSupportedError</c> in the <c>intrinsicSizes()</c> callback, a
    /// <c>DataCloneError</c> when the data of the constraints cannot be cloned.
    /// </returns>
    [IdlMember("layoutNextFragment", "https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutchild-layoutnextfragment")]
    public Task<LayoutFragment> LayoutNextFragment(LayoutConstraintsOptions? constraints = null, ChildBreakToken? breakToken = null)
    {
        var context = _worklet.CurrentContext;
        if (context is null || Context != context)
            return Task.FromException<LayoutFragment>(new DomException("The LayoutChild does not belong to the layout that is running", "InvalidStateError"));

        if (breakToken is not null && breakToken.Context != context)
            return Task.FromException<LayoutFragment>(new DomException("The ChildBreakToken does not belong to the layout that is running", "InvalidStateError"));

        if (context.Mode == LayoutApiMode.IntrinsicSizes)
            return Task.FromException<LayoutFragment>(new DomException("layoutNextFragment() cannot be called by the intrinsicSizes() callback", "NotSupportedError"));

        // https://drafts.css-houdini.org/css-layout-api-1/#translate-a-layoutconstraintsoptions-to-internal-constraints: "If the
        // child layout is a layout API container, then [...] store the data [...]: Invoking StructuredSerializeForStorage"
        constraints ??= new LayoutConstraintsOptions();
        object? data = null;
        if (constraints.Data is not null && Box.LayoutApi is not null)
        {
            try
            {
                data = context.GlobalScope.SerializeData(constraints.Data);
            }
#pragma warning disable CA1031 // The exception of the serialization (a DataCloneError) rejects the promise
            catch (Exception ex)
#pragma warning restore CA1031
            {
                return Task.FromException<LayoutFragment>(ex);
            }
        }

        var task = new LayoutApiWorkTask(this, constraints) { ConstraintsData = data };
        context.WorkQueue.Add(task);
        return task.FragmentCompletion.Task;
    }
}

/// <summary>
/// A fragment of a child of a layout API container (https://drafts.css-houdini.org/css-layout-api-1/#layoutfragment), produced
/// by <see cref="LayoutChild.LayoutNextFragment"/>: the layout reads its size, sets its position, and returns it in the
/// <c>childFragments</c> of its result.
/// </summary>
[IdlInterface("LayoutFragment", "https://drafts.css-houdini.org/css-layout-api-1/#layoutfragment")]
public sealed class LayoutFragment
{
    internal LayoutFragment(LayoutApiContext context, BoxFragment fragment, double inlineSize, double blockSize, object? data)
    {
        Context = context;
        Fragment = fragment;
        InlineSize = inlineSize;
        BlockSize = blockSize;
        Data = data;
    }

    /// <summary>The layout API context that produced the fragment (the [[unique id]] internal slot).</summary>
    internal LayoutApiContext Context { get; }

    internal BoxFragment Fragment { get; }

    /// <summary>Gets the inline size of the border box of the fragment, in the writing mode of the layout API container.</summary>
    [IdlMember("inlineSize", "https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutfragment-inlinesize")]
    public double InlineSize { get; }

    /// <summary>Gets the block size of the border box of the fragment, in the writing mode of the layout API container.</summary>
    [IdlMember("blockSize", "https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutfragment-blocksize")]
    public double BlockSize { get; }

    /// <summary>
    /// Gets or sets the offset of the fragment from the inline-start edge of the border box of the layout API container, before
    /// the relative positioning and the transforms.
    /// </summary>
    [IdlMember("inlineOffset", "https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutfragment-inlineoffset")]
    public double InlineOffset { get; set; }

    /// <summary>Gets or sets the offset of the fragment from the block-start edge of the border box of the layout API container.</summary>
    [IdlMember("blockOffset", "https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutfragment-blockoffset")]
    public double BlockOffset { get; set; }

    /// <summary>Gets the data returned by the layout of the child when it is a layout API container (a value of the script engine, structured cloned), or null.</summary>
    [IdlMember("data", "https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutfragment-data")]
    public object? Data { get; }

    /// <summary>Gets the break token of the fragment: null, the children of a layout API container are not fragmented by its layout.</summary>
    [IdlMember("breakToken", "https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutfragment-breaktoken")]
    public ChildBreakToken? BreakToken => null;
}

/// <summary>
/// The place where a child of a layout API container fragmented (https://drafts.css-houdini.org/css-layout-api-1/#childbreaktoken).
/// The layout does not fragment the children: no token is created.
/// </summary>
[IdlInterface("ChildBreakToken", "https://drafts.css-houdini.org/css-layout-api-1/#childbreaktoken")]
public sealed class ChildBreakToken
{
    internal ChildBreakToken(LayoutApiContext context, LayoutChild child, BreakType breakType)
    {
        Context = context;
        Child = child;
        BreakType = breakType;
    }

    /// <summary>The layout API context that produced the token (the [[unique id]] internal slot).</summary>
    internal LayoutApiContext Context { get; }

    /// <summary>Gets the type of the break.</summary>
    [IdlMember("breakType", "https://drafts.css-houdini.org/css-layout-api-1/#dom-childbreaktoken-breaktype")]
    public BreakType BreakType { get; }

    /// <summary>Gets the child that fragmented.</summary>
    [IdlMember("child", "https://drafts.css-houdini.org/css-layout-api-1/#dom-childbreaktoken-child")]
    public LayoutChild Child { get; }
}

/// <summary>
/// The place where a layout API container fragmented (https://drafts.css-houdini.org/css-layout-api-1/#breaktoken): the last
/// argument of the <c>layout()</c> callback. The layout API containers are not fragmented by their layout: the argument is
/// always null.
/// </summary>
[IdlInterface("BreakToken", "https://drafts.css-houdini.org/css-layout-api-1/#breaktoken")]
public sealed class BreakToken
{
    internal BreakToken(IReadOnlyList<ChildBreakToken> childBreakTokens, object? data)
    {
        ChildBreakTokens = childBreakTokens;
        Data = data;
    }

    /// <summary>Gets the break tokens of the children.</summary>
    [IdlMember("childBreakTokens", "https://drafts.css-houdini.org/css-layout-api-1/#dom-breaktoken-childbreaktokens")]
    public IReadOnlyList<ChildBreakToken> ChildBreakTokens { get; }

    /// <summary>Gets the data of the token (a value of the script engine).</summary>
    [IdlMember("data", "https://drafts.css-houdini.org/css-layout-api-1/#dom-breaktoken-data")]
    public object? Data { get; }
}

/// <summary>https://drafts.css-houdini.org/css-layout-api-1/#dictdef-breaktokenoptions</summary>
[IdlInterface("BreakTokenOptions", "https://drafts.css-houdini.org/css-layout-api-1/#dictdef-breaktokenoptions")]
public sealed class BreakTokenOptions
{
    /// <summary>Gets the break tokens of the children, or null when they are not set.</summary>
    [IdlMember("childBreakTokens", "https://drafts.css-houdini.org/css-layout-api-1/#dom-breaktokenoptions-childbreaktokens")]
    public IReadOnlyList<ChildBreakToken>? ChildBreakTokens { get; init; }

    /// <summary>Gets the data of the token (a value of the script engine).</summary>
    [IdlMember("data", "https://drafts.css-houdini.org/css-layout-api-1/#dom-breaktokenoptions-data")]
    public object? Data { get; init; }
}

/// <summary>
/// The value returned by the <c>layout()</c> callback of a layout class
/// (https://drafts.css-houdini.org/css-layout-api-1/#dictdef-fragmentresultoptions): the size of the fragment of the layout
/// API container and the fragments of its children.
/// </summary>
[IdlInterface("FragmentResultOptions", "https://drafts.css-houdini.org/css-layout-api-1/#dictdef-fragmentresultoptions")]
public sealed class FragmentResultOptions
{
    /// <summary>Gets the border-box inline size of the fragment (with the <c>manual</c> sizing).</summary>
    [IdlMember("inlineSize", "https://drafts.css-houdini.org/css-layout-api-1/#dom-fragmentresultoptions-inlinesize")]
    public double InlineSize { get; init; }

    /// <summary>Gets the border-box block size of the fragment (with the <c>manual</c> sizing).</summary>
    [IdlMember("blockSize", "https://drafts.css-houdini.org/css-layout-api-1/#dom-fragmentresultoptions-blocksize")]
    public double BlockSize { get; init; }

    /// <summary>Gets the border-box block size of the fragment when the block size of the box is <c>auto</c> (with the <c>block-like</c> sizing).</summary>
    [IdlMember("autoBlockSize", "https://drafts.css-houdini.org/css-layout-api-1/#dom-fragmentresultoptions-autoblocksize")]
    public double AutoBlockSize { get; init; }

    /// <summary>Gets the fragments of the children, in their painting order.</summary>
    [IdlMember("childFragments", "https://drafts.css-houdini.org/css-layout-api-1/#dom-fragmentresultoptions-childfragments")]
    public IReadOnlyList<LayoutFragment> ChildFragments { get; init; } = [];

    /// <summary>Gets the data given to the layout of the parent layout API container (a value of the script engine, structured cloned).</summary>
    [IdlMember("data", "https://drafts.css-houdini.org/css-layout-api-1/#dom-fragmentresultoptions-data")]
    public object? Data { get; init; }

    /// <summary>Gets the break token of the fragment, or null.</summary>
    [IdlMember("breakToken", "https://drafts.css-houdini.org/css-layout-api-1/#dom-fragmentresultoptions-breaktoken")]
    public BreakTokenOptions? BreakToken { get; init; }
}

/// <summary>
/// The result of a layout (https://drafts.css-houdini.org/css-layout-api-1/#fragmentresult): a <c>layout()</c> callback
/// returns a <see cref="FragmentResultOptions"/> dictionary, or constructs a <c>FragmentResult</c> from it to read the size
/// the fragment will have.
/// </summary>
[IdlInterface("FragmentResult", "https://drafts.css-houdini.org/css-layout-api-1/#fragmentresult")]
public sealed class FragmentResult
{
    /// <summary>
    /// Constructs the result of the layout that is running (https://drafts.css-houdini.org/css-layout-api-1/#dom-fragmentresult-fragmentresult,
    /// "construct a fragment result").
    /// </summary>
    /// <param name="options">The size of the fragment and the fragments of the children.</param>
    /// <exception cref="TypeErrorException">A child fragment or a break token was not produced by the layout that is running, or a child fragment is listed twice.</exception>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when no <c>layout()</c> callback is running; a <c>DataCloneError</c> when the data cannot be cloned.</exception>
    [IdlMember("constructor", "https://drafts.css-houdini.org/css-layout-api-1/#dom-fragmentresult-fragmentresult")]
    public FragmentResult(FragmentResultOptions? options = null)
        : this(Document.CurrentGlobalDocument.LayoutWorkletOrNull?.CurrentContext, options ?? new FragmentResultOptions())
    {
    }

    internal FragmentResult(LayoutApiContext? context, FragmentResultOptions options)
    {
        if (context is null || context.Mode != LayoutApiMode.Layout)
            throw new DomException("A FragmentResult can only be constructed by a layout() callback", "InvalidStateError");

        var fragments = new HashSet<LayoutFragment>(ReferenceEqualityComparer.Instance);
        var boxes = new HashSet<ContainerBox>(ReferenceEqualityComparer.Instance);
        foreach (var childFragment in options.ChildFragments)
        {
            if (childFragment.Context != context)
                throw new TypeErrorException("A child fragment was not produced by the layout that is running");

            // A box generates one fragment in a layout API container: its children are not fragmented
            if (!fragments.Add(childFragment) || !boxes.Add(childFragment.Fragment.Box))
                throw new TypeErrorException("A child has more than one fragment");
        }

        if (options.BreakToken?.ChildBreakTokens is { } childBreakTokens)
        {
            foreach (var childBreakToken in childBreakTokens)
            {
                if (childBreakToken.Context != context)
                    throw new TypeErrorException("A child break token was not produced by the layout that is running");
            }
        }

        Context = context;
        ChildFragments = options.ChildFragments;
        AutoBlockSize = options.AutoBlockSize;
        if (context.Sizing == LayoutSizingMode.BlockLike)
        {
            // "calculating box's border-box inline size / block size exactly like block containers do, given fragment's
            // autoBlockSize as the intrinsic block size"
            InlineSize = context.BorderBoxInlineSize;
            BlockSize = context.ComputeBorderBoxBlockSize(options.AutoBlockSize);
        }
        else
        {
            InlineSize = Math.Max(0, options.InlineSize);
            BlockSize = Math.Max(0, options.BlockSize);
        }

        Data = options.Data is null ? null : context.GlobalScope.SerializeData(options.Data);
        if (options.BreakToken?.Data is { } breakTokenData)
        {
            // The data of the break token is cloned, but the layout API containers are not fragmented by their layout
            context.GlobalScope.SerializeData(breakTokenData);
        }
    }

    /// <summary>The layout API context of the result (the [[unique id]] internal slot).</summary>
    internal LayoutApiContext Context { get; }

    /// <summary>The fragments of the children (the [[child fragments]] internal slot).</summary>
    internal IReadOnlyList<LayoutFragment> ChildFragments { get; }

    /// <summary>The serialized data (the [[data]] internal slot), or null.</summary>
    internal object? Data { get; }

    internal double AutoBlockSize { get; }

    /// <summary>Gets the border-box inline size the fragment will have.</summary>
    [IdlMember("inlineSize", "https://drafts.css-houdini.org/css-layout-api-1/#dom-fragmentresult-inlinesize")]
    public double InlineSize { get; }

    /// <summary>Gets the border-box block size the fragment will have.</summary>
    [IdlMember("blockSize", "https://drafts.css-houdini.org/css-layout-api-1/#dom-fragmentresult-blocksize")]
    public double BlockSize { get; }
}
