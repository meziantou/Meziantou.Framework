using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The layout of a layout API container (https://drafts.css-houdini.org/css-layout-api-1/#layout-api-containers): a box whose
/// display is <c>layout(name)</c> and whose layout is registered in the layout worklet of the document
/// (<see cref="ContainerBox.LayoutApi"/>). The box "establishes a new layout API formatting context for its contents. This is
/// the same as establishing a block formatting context, except that the layout provided by the author is used instead of the
/// block layout".
/// </summary>
/// <remarks>
/// <para>
/// The shared sizing of <see cref="FormattingContext.Layout"/> sizes the box like a block container (the <c>block-like</c>
/// sizing): its <c>auto</c> inline size fills the available space or is its fit-content size, computed from the sizes that
/// the <c>intrinsicSizes()</c> callback returns (<see cref="ComputeIntrinsicInlineSizes"/>); the <c>layout()</c> callback then
/// lays out the children in the box (<see cref="LayoutContents"/>) and returns the fragments of the children, positioned
/// from the border box, and the <c>autoBlockSize</c> of the box.
/// </para>
/// <para>
/// The callbacks run in a global scope of the layout worklet, synchronously: they are async functions that await the
/// promises of <see cref="LayoutChild.LayoutNextFragment"/> and <see cref="LayoutChild.IntrinsicSizes"/>, which the layout
/// resolves by running the work queue (https://drafts.css-houdini.org/css-layout-api-1/#run-a-work-queue) and the microtasks
/// of the worklet, in a microtask queue of their own (<see cref="Scripting.ScriptEngine.EnterNestedMicrotaskQueue"/>), until
/// the promise of the callback is settled.
/// </para>
/// <para>
/// When a callback fails (an exception, a promise that is rejected or never settled, an invalid result), the box falls back to
/// the flow layout: it is laid out by <see cref="BlockFormattingContext"/> for the rest of the layout pass. Its children still
/// establish independent formatting contexts.
/// </para>
/// </remarks>
internal sealed class LayoutApiFormattingContext : FormattingContext
{
    public static LayoutApiFormattingContext Instance { get; } = new();

    private LayoutApiFormattingContext()
    {
    }

    public override FormattingContextKind Kind => FormattingContextKind.Block;

    public override BoxFragment Layout(LayoutContext context, ContainerBox box, in ConstraintSpace space)
    {
        // (the callbacks of the layout class are invoked by each layout)
        context.PreventLayoutCaching();
        // The constraints of the layout() callback include the sizes that the parent gives to the box
        var api = box.LayoutApi!;
        var previousSpace = api.Space;
        api.Space = space;
        try
        {
            if (api.Definition.Options.Sizing != LayoutSizingMode.Manual || api.ReusesManualResult)
                return base.Layout(context, box, space);

            // https://drafts.css-houdini.org/css-layout-api-1/#sizing: with the manual sizing, the layout returns the sizes of the
            // box. The box is first sized like a block container, which gives the sizes that its formatting context forces and its
            // box model edges; the callback is invoked by the layout of the contents, and the sizes of its result are then
            // imposed to the box
            api.ManualResult = null;
            var fragment = base.Layout(context, box, space);
            if (api.ManualResult is not { } result)
                return fragment;

            var isOrthogonal = WritingDirection.FromStyle(box.Style.Layout).IsOrthogonalTo(space.WritingDirection);
            var imposed = isOrthogonal
                ? space with { FixedInlineSize = result.BlockSize, FixedBlockSize = result.InlineSize }
                : space with { FixedInlineSize = result.InlineSize, FixedBlockSize = result.BlockSize };
            api.ReusesManualResult = true;
            api.Space = imposed;
            try
            {
                return base.Layout(context, box, imposed);
            }
            finally
            {
                api.ReusesManualResult = false;
                api.ManualResult = null;
            }
        }
        finally
        {
            api.Space = previousSpace;
        }
    }

    protected override ContentLayoutResult LayoutContents(LayoutContext context, ContainerBox box, FragmentBuilder builder, in ContentArea content)
    {
        var api = box.LayoutApi!;
        var isManual = api.Definition.Options.Sizing == LayoutSizingMode.Manual;
        FragmentResult? result;
        if (api.ReusesManualResult)
        {
            result = api.ManualResult;
        }
        else
        {
            result = api.FallbackPass == context.Pass ? null : GenerateFragment(context, box, api, content);
            if (result is not null && isManual)
            {
                // The sizes of the result are imposed to the box by a second layout (see Layout)
                api.ManualResult = result;
                return new ContentLayoutResult(0);
            }
        }

        if (result is null)
        {
            // "let box fallback to the flow layout"
            api.FallbackPass = context.Pass;
            api.ResultData = null;
            return BlockFormattingContext.Instance.LayoutFlow(context, box, builder, content);
        }

        // https://drafts.css-houdini.org/css-layout-api-1/#generate-a-fragment: the fragments of the children are positioned
        // relative to the border box of the fragment, with the inlineOffset and the blockOffset the author set; their order is
        // their painting order. The baselines are the ones of a block container, in the order of the fragments
        // (https://drafts.css-houdini.org/css-layout-api-1/#interaction-alignment)
        var writingDirection = content.WritingDirection;
        double? firstBaseline = null;
        double? lastBaseline = null;
        foreach (var childFragment in result.ChildFragments)
        {
            var fragment = childFragment.Fragment;
            builder.AddChild(fragment, new LogicalOffset(childFragment.InlineOffset, childFragment.BlockOffset));
            if (fragment.WritingDirection.IsOrthogonalTo(writingDirection))
                continue;

            if (firstBaseline is null && fragment.FirstBaseline is { } first)
            {
                firstBaseline = childFragment.BlockOffset + first - content.Offset.Block;
            }

            if (fragment.LastBaseline is { } last)
            {
                lastBaseline = childFragment.BlockOffset + last - content.Offset.Block;
            }
        }

        // https://drafts.css-houdini.org/css-layout-api-1/#interaction-positioning: "The static position of an
        // absolutely-positioned child of a layout API container is set to the inline-start, block-start padding edge of the
        // layout API container"
        var padding = writingDirection.ToLogical(ResolvePadding(box.Style.Layout.Box, api.Space.PercentageInlineSize));
        var paddingEdge = new LogicalOffset(content.Offset.Inline - padding.InlineStart, content.Offset.Block - padding.BlockStart);
        foreach (var child in box.Children)
        {
            if (child is ContainerBox childBox && PositionedLayout.IsAbsolutelyPositioned(childBox))
            {
                builder.AddOutOfFlowCandidate(childBox, paddingEdge);
            }
        }

        api.ResultData = result.Data;
        var blockEdges = content.Edges.BlockSum();
        var contentBlockSize = isManual ? content.BlockSize ?? Math.Max(0, result.BlockSize - blockEdges) : Math.Max(0, result.AutoBlockSize - blockEdges);
        return new ContentLayoutResult(contentBlockSize, firstBaseline, lastBaseline);
    }

    /// <summary>
    /// Returns the min-content and max-content inline sizes of the content box of a layout API container
    /// (https://drafts.css-houdini.org/css-layout-api-1/#determine-the-intrinsic-sizes): the sizes that the
    /// <c>intrinsicSizes()</c> callback returns, which are border-box sizes (the callback gets the box model edges), or the
    /// sizes of the flow layout when the box falls back to it.
    /// </summary>
    protected internal override IntrinsicSizes ComputeIntrinsicInlineSizes(LayoutContext context, ContainerBox box, in IntrinsicSizingInput input)
    {
        var api = box.LayoutApi!;
        if (api.FallbackPass != context.Pass && DetermineIntrinsicSizes(context, box, api, input.ContentBlockSize) is { } sizes)
            return sizes;

        api.FallbackPass = context.Pass;
        return BlockFormattingContext.Instance.ComputeIntrinsicInlineSizes(context, box, input);
    }

    /// <summary>https://drafts.css-houdini.org/css-layout-api-1/#invoke-an-intrinsic-sizes-callback: returns null when the box falls back to the flow layout.</summary>
    private static IntrinsicSizes? DetermineIntrinsicSizes(LayoutContext context, ContainerBox box, LayoutApiContainer api, double? contentBlockSize)
    {
        if (!TryGetDefinition(context, api, out var globalScope, out var definition) || globalScope.GetLayoutClassInstance(box, api.Name, definition) is not { } instance)
            return null;

        // The box model edges: the percentages of the paddings resolve against zero, as for the intrinsic size contributions
        var layout = box.Style.Layout;
        var writingDirection = WritingDirection.FromStyle(layout);
        var paddingAndBorder = writingDirection.ToLogical(ResolvePadding(layout.Box, 0).Add(layout.Box.BorderWidth));
        var gutters = IntrinsicSizing.GetScrollbarGutters(context, box, writingDirection);
        var edges = new LogicalSides<double>(paddingAndBorder.BlockStart + gutters.BlockStart, paddingAndBorder.InlineEnd + gutters.InlineEnd, paddingAndBorder.BlockEnd + gutters.BlockEnd, paddingAndBorder.InlineStart + gutters.InlineStart);

        var apiContext = new LayoutApiContext(LayoutApiMode.IntrinsicSizes, globalScope, context, box, definition.Options.Sizing) { ContentBlockSize = contentBlockSize };
        var children = GetChildren(globalScope, box, definition, apiContext);
        var styleMap = new PaintStylePropertyMap(box.Style, definition.InputProperties);
        if (!TryRunCallback(apiContext, definition, definition.IntrinsicSizesFunction, instance, [children, new LayoutEdges(edges), styleMap], out var value))
            return null;

        IntrinsicSizesResultOptions options;
        try
        {
            options = definition.ClassConstructor.ConvertToDictionary<IntrinsicSizesResultOptions>(value);
        }
#pragma warning disable CA1031 // "If an exception is thrown, let box fallback to the flow layout"
        catch (Exception ex)
#pragma warning restore CA1031
        {
            definition.ClassConstructor.ReportException(ex);
            return null;
        }

        // The sizes of the content box: a min-content size is not larger than the max-content size
        var maxContent = Math.Max(0, (options.MaxContentSize ?? 0) - edges.InlineSum());
        var minContent = Math.Min(maxContent, Math.Max(0, (options.MinContentSize ?? 0) - edges.InlineSum()));
        return new IntrinsicSizes(minContent, maxContent);
    }

    /// <summary>
    /// https://drafts.css-houdini.org/css-layout-api-1/#invoke-a-layout-callback: returns the result of the <c>layout()</c>
    /// callback for the content box of the box, or null when the box falls back to the flow layout.
    /// </summary>
    private static FragmentResult? GenerateFragment(LayoutContext context, ContainerBox box, LayoutApiContainer api, in ContentArea content)
    {
        if (!TryGetDefinition(context, api, out var globalScope, out var definition) || globalScope.GetLayoutClassInstance(box, api.Name, definition) is not { } instance)
            return null;

        var edges = content.Edges;
        var sizing = definition.Options.Sizing;
        var space = api.Space;
        var isOrthogonal = content.WritingDirection.IsOrthogonalTo(space.WritingDirection);
        var borderBoxInlineSize = content.InlineSize + edges.InlineSum();
        double? borderBoxBlockSize = content.BlockSize is { } contentBlockSize ? contentBlockSize + edges.BlockSum() : null;

        // https://drafts.css-houdini.org/css-layout-api-1/#sizing: "If the layout API container has an auto block size, and
        // cannot be determined ahead of time, fixedBlockSize must be set to null". A parent that imposes to the box the block
        // size the box got from its own layout (a stretched item that is alone in its flex line or its grid row, the main size
        // of an item of a column flex container) does not determine it ahead of time: the constraints are the ones of that
        // layout, as in Chromium (WPT css/css-layout-api/constraints/fixed-block-size-flex-none.https.html)
        var isBlockSizeImposed = (isOrthogonal ? space.FixedInlineSize : space.FixedBlockSize) is not null;
        if (borderBoxBlockSize is { } imposedBlockSize && isBlockSizeImposed
            && (content.IsBlockSizeIndefinite || api.AutoBlockSizePass == context.Pass && Math.Abs(api.AutoBlockSize - imposedBlockSize) < 0.01 && Math.Abs(api.AutoBlockSizeInlineSize - borderBoxInlineSize) < 0.01))
        {
            borderBoxBlockSize = null;
        }

        // https://quirks.spec.whatwg.org/#the-body-element-fills-the-html-element-quirk: the block size of the body element that
        // fills the html element is known before its layout
        if (borderBoxBlockSize is null && !isBlockSizeImposed && content.BlockSize is null && content.StretchUsesQuirksPercentageBlockSize && content.QuirksPercentageBlockSize is { } quirksBlockSize)
        {
            borderBoxBlockSize = quirksBlockSize + edges.BlockSum();
        }

        // https://drafts.css-houdini.org/css-layout-api-1/#create-a-layout-constraints-object: the available sizes and the
        // percentage resolution sizes of the constraints of the parent, relative to the writing mode of the box
        var availableInlineSize = isOrthogonal ? space.AvailableBlockSize ?? 0 : space.AvailableInlineSize;
        var availableBlockSize = isOrthogonal ? space.AvailableInlineSize : space.AvailableBlockSize ?? 0;
        var percentageInlineSize = isOrthogonal ? space.PercentageBlockSize ?? 0 : space.IsInlineSizeIndefinite ? 0 : space.PercentageInlineSize;
        var percentageBlockSize = isOrthogonal ? (space.IsInlineSizeIndefinite ? 0 : space.PercentageInlineSize) : space.PercentageBlockSize ?? 0;
        double? fixedInlineSize;
        double? fixedBlockSize;
        if (sizing == LayoutSizingMode.BlockLike)
        {
            fixedInlineSize = borderBoxInlineSize;
            fixedBlockSize = borderBoxBlockSize;
            availableInlineSize = borderBoxInlineSize;
            availableBlockSize = borderBoxBlockSize ?? availableBlockSize;
        }
        else
        {
            // The sizes are only fixed when the formatting context the box participates in forces them: the sizes imposed by the
            // parent (a flex or grid item, a child of a layout API container), the stretch-fit inline size of an in-flow
            // block-level box whose inline size is auto in a block formatting context, and the sizes that the insets of an
            // absolutely positioned box give
            var isInlineSizeForced = (isOrthogonal ? space.FixedBlockSize : space.FixedInlineSize) is not null
                || (isOrthogonal ? space.StretchBlockSize : space.StretchInlineSize) is not null
                || (!isOrthogonal && box.IsBlockLevel && box.IsInFlow && !space.AutoInlineSizeIsFitContent && !space.IsInlineSizeIndefinite && !BlockFormattingContext.UsesShrinkToFit(box)
                    && box.Style.Layout.Box.GetSize(content.WritingDirection.IsHorizontal).IsAuto);
            var isBlockSizeForced = (isOrthogonal ? space.FixedInlineSize : space.FixedBlockSize) is not null
                || (isOrthogonal ? space.StretchInlineSize : space.StretchBlockSize) is not null;
            fixedInlineSize = isInlineSizeForced ? borderBoxInlineSize : null;
            fixedBlockSize = isBlockSizeForced ? borderBoxBlockSize : null;
        }

        var apiContext = new LayoutApiContext(LayoutApiMode.Layout, globalScope, context, box, sizing)
        {
            BorderBoxInlineSize = borderBoxInlineSize,
            BorderBoxBlockSize = borderBoxBlockSize,
            BlockEdges = edges.BlockSum(),
            MinBorderBoxBlockSize = content.MinBlockSize + edges.BlockSum(),
            MaxBorderBoxBlockSize = content.MaxBlockSize + edges.BlockSum(),
        };

        var constraints = new LayoutConstraints
        {
            AvailableInlineSize = availableInlineSize,
            AvailableBlockSize = availableBlockSize,
            FixedInlineSize = fixedInlineSize,
            FixedBlockSize = fixedBlockSize,
            PercentageInlineSize = percentageInlineSize,
            PercentageBlockSize = percentageBlockSize,
            BlockFragmentationType = BlockFragmentationType.None,

            // "If internalFragment has a clonedData object stored with it": the data of the constraints of the parent layout
            Data = api.ConstraintsData is { } constraintsData ? globalScope.DeserializeData(constraintsData) : null,
        };

        var children = GetChildren(globalScope, box, definition, apiContext);
        var styleMap = new PaintStylePropertyMap(box.Style, definition.InputProperties);
        if (!TryRunCallback(apiContext, definition, definition.LayoutFunction, instance, [children, new LayoutEdges(edges), constraints, styleMap, null], out var value))
            return null;

        // "If fragmentResultValue is a platform object", it is a FragmentResult; else it is converted to a FragmentResultOptions,
        // and the fragment result is constructed
        var worklet = globalScope.LayoutWorklet;
        var previousContext = worklet.CurrentContext;
        worklet.CurrentContext = apiContext;
        try
        {
            var result = definition.ClassConstructor.AsPlatformObject(value) is { } platformObject
                ? platformObject as FragmentResult ?? throw new TypeErrorException("The value returned by layout() is not a FragmentResult")
                : new FragmentResult(apiContext, definition.ClassConstructor.ConvertToDictionary<FragmentResultOptions>(value));
            if (result.Context != apiContext)
                throw new TypeErrorException("The FragmentResult returned by layout() was not constructed by the layout that is running");

            if (borderBoxBlockSize is null && !isBlockSizeImposed)
            {
                // The block size of the box that its own layout gives (see above)
                api.AutoBlockSizePass = context.Pass;
                api.AutoBlockSize = apiContext.ComputeBorderBoxBlockSize(result.AutoBlockSize);
                api.AutoBlockSizeInlineSize = borderBoxInlineSize;
            }

            return result;
        }
#pragma warning disable CA1031 // "If an exception is thrown, let box fallback to the flow layout"
        catch (Exception ex)
#pragma warning restore CA1031
        {
            definition.ClassConstructor.ReportException(ex);
            return null;
        }
        finally
        {
            worklet.CurrentContext = previousContext;
        }
    }

    /// <summary>
    /// Selects a global scope of the layout worklet and returns its layout definition
    /// (https://drafts.css-houdini.org/css-layout-api-1/#get-a-layout-definition): a layout that is not registered in the global
    /// scope makes the document layout definition invalid.
    /// </summary>
    private static bool TryGetDefinition(LayoutContext context, LayoutApiContainer api, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out LayoutWorkletGlobalScope? globalScope, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out LayoutDefinition? definition)
    {
        definition = null;
        globalScope = null;
        var worklet = context.Document.LayoutWorkletOrNull;
        if (worklet is null || worklet.GetDocumentLayoutDefinition(api.Name) is null)
            return false;

        globalScope = worklet.SelectGlobalScope();
        if (globalScope is null)
            return false;

        if (globalScope.LayoutDefinitions.TryGetValue(api.Name, out definition))
            return true;

        var name = api.Name;
        globalScope.QueueGlobalTask(() => worklet.InvalidateDocumentLayoutDefinition(name, $"The layout '{name}' is not registered in all the global scopes of the layout worklet."));
        return false;
    }

    /// <summary>The children of a layout API container given to its callbacks: its in-flow children ("Out-of-flow children do not appear as LayoutChildren"; the floats are in flow).</summary>
    private static List<LayoutChild> GetChildren(LayoutWorkletGlobalScope globalScope, ContainerBox box, LayoutDefinition definition, LayoutApiContext apiContext)
    {
        var children = new List<LayoutChild>();
        foreach (var child in box.Children)
        {
            if (child is ContainerBox childBox && !childBox.IsOutsideMarker && !PositionedLayout.IsAbsolutelyPositioned(childBox))
            {
                children.Add(globalScope.GetLayoutChild(childBox, definition, apiContext));
            }
        }

        return children;
    }

    /// <summary>
    /// Invokes a callback of a layout class and, when it returns a promise, runs the work queue of the layout API context
    /// until the promise is settled (https://drafts.css-houdini.org/css-layout-api-1/#run-a-work-queue). Returns false when the
    /// callback throws, or when its promise is rejected or still pending once the work queue is empty.
    /// </summary>
    private static bool TryRunCallback(LayoutApiContext apiContext, LayoutDefinition definition, object function, object instance, object?[] arguments, out object? value)
    {
        value = null;
        var globalScope = apiContext.GlobalScope;
        var worklet = globalScope.LayoutWorklet;
        if (globalScope.ScriptEngine is not { } engine)
            return false;

        // The worklet has its own microtask queue: the microtasks of the document do not run during its layout
        var classConstructor = definition.ClassConstructor;
        var previousContext = worklet.CurrentContext;
        worklet.CurrentContext = apiContext;
        var microtaskQueue = engine.EnterNestedMicrotaskQueue();
        try
        {
            object? result;
            using (AuthorCodeScope.Enter())
            {
                result = classConstructor.Invoke(function, instance, arguments);
            }

            var promise = classConstructor.ObservePromise(result);
            engine.PerformMicrotaskCheckpoint();
            if (promise is null)
            {
                value = result;
                return true;
            }

            var workQueue = apiContext.WorkQueue;
            while (workQueue.Count > 0 && !promise.IsSettled)
            {
                // The promises of the tasks are resolved once all the tasks are run: the layout of a child may run the microtasks
                // of the worklet (a child that is a layout API container)
                var tasks = workQueue.ToArray();
                workQueue.Clear();
                foreach (var task in tasks)
                {
                    RunWorkTask(apiContext, task);
                }

                foreach (var task in tasks)
                {
                    task.Complete();
                }

                engine.PerformMicrotaskCheckpoint();
            }

            if (!promise.IsFulfilled)
            {
                // The rejection reason is reported as the exception of a callback that throws
                if (promise.IsSettled)
                {
                    classConstructor.ReportRejection(promise.Value);
                }
                else
                {
                    worklet.ReportError($"The promise of a callback of the layout '{apiContext.Box.LayoutApi?.Name}' was not settled: the box falls back to the flow layout.");
                }

                return false;
            }

            value = promise.Value;
            return true;
        }
#pragma warning disable CA1031 // "If an exception is thrown the let box fallback to the flow layout"
        catch (Exception ex)
#pragma warning restore CA1031
        {
            classConstructor.ReportException(ex);
            return false;
        }
        finally
        {
            microtaskQueue.Dispose();
            worklet.CurrentContext = previousContext;
        }
    }

    /// <summary>Runs a task of the work queue: the layout of a child, or the computation of its intrinsic sizes.</summary>
    private static void RunWorkTask(LayoutApiContext apiContext, LayoutApiWorkTask task)
    {
        try
        {
            var childBox = task.Child.Box;
            var context = apiContext.LayoutContext;
            var writingDirection = apiContext.WritingDirection;
            if (task.Constraints is not { } options)
            {
                // "the border box min/max content contribution of box", in the inline axis of the layout API container
                // (the percentage block sizes of the child resolve against the block size of the content box when it is definite)
                var contribution = IntrinsicSizing.GetContribution(context, childBox, writingDirection, apiContext.ContentBlockSize);
                var margin = ResolveMargins(childBox.Style.Layout.Box, 0);
                var marginSum = writingDirection.IsHorizontal ? margin.HorizontalSum() : margin.VerticalSum();
                task.Result = new LayoutIntrinsicSizes(Math.Max(0, contribution.MinContent - marginSum), Math.Max(0, contribution.MaxContent - marginSum));
                return;
            }

            var childApi = childBox.LayoutApi;
            if (childApi is not null)
            {
                childApi.ConstraintsData = task.ConstraintsData;
                childApi.ResultData = null;
            }

            BoxFragment fragment;
            try
            {
                fragment = context.LayoutBox(childBox, CreateChildSpace(context, writingDirection, options));
            }
            finally
            {
                childApi?.ConstraintsData = null;
            }

            var size = writingDirection.ToLogical(fragment.Size);
            var data = childApi?.ResultData is { } serialized ? apiContext.GlobalScope.DeserializeData(serialized) : null;
            task.Result = new LayoutFragment(apiContext, fragment, size.Inline, size.Block, data);
        }
#pragma warning disable CA1031 // The exception rejects the promise of the task
        catch (Exception ex)
#pragma warning restore CA1031
        {
            task.Exception = ex;
        }
    }

    /// <summary>
    /// https://drafts.css-houdini.org/css-layout-api-1/#translate-a-layoutconstraintsoptions-to-internal-constraints: the
    /// constraint space of a child, in the writing mode of the layout API container. An available size or a percentage
    /// resolution size that is not set, or not greater than zero, is zero; an <c>auto</c> inline size of the child is its
    /// fit-content size in the available inline size.
    /// </summary>
    private static ConstraintSpace CreateChildSpace(LayoutContext context, WritingDirection writingDirection, LayoutConstraintsOptions options)
    {
        var availableInlineSize = options.AvailableInlineSize is > 0 and var inline ? inline : 0;
        var availableBlockSize = options.AvailableBlockSize is > 0 and var block ? block : 0;
        var percentageBlockSize = options.PercentageBlockSize is > 0 and var percentageBlock ? percentageBlock : availableBlockSize;
        return new ConstraintSpace
        {
            WritingDirection = writingDirection,
            AvailableInlineSize = availableInlineSize,
            AvailableBlockSize = availableBlockSize,
            PercentageInlineSize = options.PercentageInlineSize is > 0 and var percentageInline ? percentageInline : availableInlineSize,
            PercentageBlockSize = percentageBlockSize,

            // https://quirks.spec.whatwg.org/#the-percentage-height-calculation-quirk: the percentage heights of the descendants of
            // the child whose containing blocks have an auto height resolve against the percentage resolution size
            QuirksPercentageBlockSize = context.IsQuirksMode ? percentageBlockSize : null,
            FixedInlineSize = options.FixedInlineSize is { } fixedInline ? Math.Max(0, fixedInline) : null,
            FixedBlockSize = options.FixedBlockSize is { } fixedBlock ? Math.Max(0, fixedBlock) : null,
            AutoInlineSizeIsFitContent = true,
        };
    }
}
