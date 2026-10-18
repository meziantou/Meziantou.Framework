using System.Runtime.CompilerServices;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The layout worklet of a document (https://drafts.css-houdini.org/css-layout-api-1/#layout-worklet, <c>CSS.layoutWorklet</c>):
/// its global scopes register the classes that lay out the <c>display: layout()</c> boxes, and the document keeps what it
/// needs to know about them (the document layout definitions).
/// </summary>
/// <remarks>
/// The callbacks of a layout class are invoked, synchronously, by the layout of the document
/// (<see cref="LayoutApiFormattingContext"/>), in a global scope selected by <see cref="SelectGlobalScope"/>.
/// </remarks>
internal sealed class LayoutWorklet : Worklet
{
    // The callbacks invoked in a global scope before another global scope is selected (https://drafts.css-houdini.org/css-layout-api-1/#global-scope-selection:
    // a global scope must not be used more than 1000 times in a row)
    private const int InvocationsPerGlobalScope = 4;

    private readonly Dictionary<string, DocumentLayoutDefinition?> _documentLayoutDefinitions = new(StringComparer.Ordinal);
    private int _invocationCount;

    public LayoutWorklet(Document document)
        : base(document)
    {
    }

    // Fetch has no destination for the layout worklets (https://fetch.spec.whatwg.org/#concept-request-destination): their
    // modules are fetched as scripts
    internal override RequestDestination Destination => RequestDestination.Script;

    // https://drafts.css-houdini.org/css-layout-api-1/#global-scope-selection: "Select from at least two LayoutWorkletGlobalScopes"
    private protected override int GlobalScopeCount => 2;

    /// <summary>
    /// Whether a <c>display: layout()</c> value was computed for an element of the document: the registration of a layout then
    /// changes the styles (the blockification of the children) and the boxes.
    /// </summary>
    public bool HasLayoutContainers { get; set; }

    /// <summary>The layout API context of the callback that is running (the "current layout"), or null.</summary>
    public LayoutApiContext? CurrentContext { get; set; }

    private protected override WorkletGlobalScope CreateGlobalScopeCore() => new LayoutWorkletGlobalScope(this);

    private protected override void OnTerminated()
    {
        _documentLayoutDefinitions.Clear();
        InvalidateLayoutContainers();
    }

    /// <summary>
    /// The task queued by <c>registerLayout()</c> (https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutworkletglobalscope-registerlayout):
    /// the definition becomes invalid when the global scopes do not register equivalent classes.
    /// </summary>
    public void RegisterDocumentLayoutDefinition(string name, DocumentLayoutDefinition definition)
    {
        if (_documentLayoutDefinitions.TryGetValue(name, out var existing))
        {
            if (existing is null || existing.IsEquivalent(definition))
                return;

            InvalidateDocumentLayoutDefinition(name, $"The layout '{name}' was registered with different inputProperties, childInputProperties or layoutOptions in the global scopes of the layout worklet.");
            return;
        }

        _documentLayoutDefinitions[name] = definition;
        InvalidateLayoutContainers();
    }

    /// <summary>
    /// Returns the document layout definition of a name (https://drafts.css-houdini.org/css-layout-api-1/#get-a-document-layout-definition),
    /// or null when the layout is not registered or its definition is invalid: the box falls back to the flow layout.
    /// </summary>
    public DocumentLayoutDefinition? GetDocumentLayoutDefinition(string name) => _documentLayoutDefinitions.GetValueOrDefault(name);

    /// <summary>Makes the document layout definition of a name invalid, and logs an error to the console of the document.</summary>
    public void InvalidateDocumentLayoutDefinition(string name, string message)
    {
        if (_documentLayoutDefinitions.TryGetValue(name, out var existing) && existing is null)
            return;

        _documentLayoutDefinitions[name] = null;
        InvalidateLayoutContainers();
        ReportError(message);
    }

    /// <summary>The layout API containers of the document are styled and laid out again: a layout was registered, or became invalid.</summary>
    private void InvalidateLayoutContainers()
    {
        if (!HasLayoutContainers)
            return;

        // The computed display of the children depends on the childDisplay of the layout, and the boxes on its registration
        CssStyleVersion.Increment(Document);
        Document.InvalidateLayout(rebuildBoxTree: true);
    }

    /// <summary>Logs an error to the console of the document ("log an error to the debugging console").</summary>
    public void ReportError(string message)
        => Document.RelevantWindow?.ReportConsoleMessage(new ConsoleMessage("error", ConsoleMessageLevel.Error, message));

    /// <summary>
    /// Selects the global scope that runs a callback (https://drafts.css-houdini.org/css-layout-api-1/#global-scope-selection):
    /// the global scopes are used in turn, so that the classes cannot rely on the state of a global scope.
    /// </summary>
    public LayoutWorkletGlobalScope? SelectGlobalScope()
    {
        if (IsTerminated || GlobalScopes.Count == 0)
            return null;

        var globalScope = (LayoutWorkletGlobalScope)GlobalScopes[_invocationCount++ / InvocationsPerGlobalScope % GlobalScopes.Count];
        return globalScope.CanRunScript ? globalScope : null;
    }
}

/// <summary>
/// The global object of a layout worklet (https://drafts.css-houdini.org/css-layout-api-1/#layoutworkletglobalscope): the
/// scripts added to <c>CSS.layoutWorklet</c> register the classes that lay out the <c>display: layout()</c> boxes.
/// </summary>
[IdlInterface("LayoutWorkletGlobalScope", "https://drafts.css-houdini.org/css-layout-api-1/#layoutworkletglobalscope")]
public sealed class LayoutWorkletGlobalScope : WorkletGlobalScope
{
    // The layout class instances of the boxes (https://drafts.css-houdini.org/css-layout-api-1/#layout-class-instances): the
    // boxes are built again when the styles change, so the instances are kept for the element of the box
    private readonly ConditionalWeakTable<Element, Dictionary<(string Name, string? PseudoElement), object>> _layoutClassInstances = [];

    // The [[layoutChildMap]] internal slots of the boxes for this global scope
    private readonly ConditionalWeakTable<ContainerBox, LayoutChild> _layoutChildren = [];

    internal LayoutWorkletGlobalScope(LayoutWorklet worklet)
        : base(worklet)
    {
        LayoutWorklet = worklet;
    }

    internal LayoutWorklet LayoutWorklet { get; }

    internal override ExposureSet Exposure => ExposureSet.LayoutWorklet;

    internal override string InterfaceName => "LayoutWorkletGlobalScope";

    /// <summary>The layout definitions of the global scope (https://drafts.css-houdini.org/css-layout-api-1/#layout-definitions), by name.</summary>
    internal Dictionary<string, LayoutDefinition> LayoutDefinitions { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a class that lays out the <c>display: layout(<paramref name="name"/>)</c> boxes
    /// (https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutworkletglobalscope-registerlayout).
    /// </summary>
    /// <param name="name">The name of the layout.</param>
    /// <param name="layoutCtor">
    /// The class: its static <c>inputProperties</c>, <c>childInputProperties</c> and <c>layoutOptions</c> properties are read
    /// once, and its prototype has the <c>intrinsicSizes</c> and <c>layout</c> callbacks.
    /// </param>
    /// <exception cref="TypeErrorException">The name is empty, the options are invalid, the prototype of the class is not an object, or it has no <c>intrinsicSizes</c> or <c>layout</c> function.</exception>
    /// <exception cref="DomException">An <c>InvalidModificationError</c> when the name is already registered in the global scope.</exception>
    [IdlMember("registerLayout", "https://drafts.css-houdini.org/css-layout-api-1/#dom-layoutworkletglobalscope-registerlayout")]
    public void RegisterLayout(string name, WorkletClassConstructor layoutCtor)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(layoutCtor);
        if (name.Length == 0)
            throw new TypeErrorException("The name of a layout cannot be empty");

        if (LayoutDefinitions.ContainsKey(name))
            throw new DomException($"The layout '{name}' is already registered", "InvalidModificationError");

        var inputProperties = FilterProperties(layoutCtor.GetStringSequence("inputProperties"));
        var childInputProperties = FilterProperties(layoutCtor.GetStringSequence("childInputProperties"));

        // The members of the LayoutOptions dictionary are converted in lexicographical order
        var childDisplay = layoutCtor.GetDictionaryString("layoutOptions", "childDisplay") switch
        {
            null or "block" => ChildDisplayType.Block,
            "normal" => ChildDisplayType.Normal,
            var value => throw new TypeErrorException($"'{value}' is not a valid value of ChildDisplayType"),
        };
        var sizing = layoutCtor.GetDictionaryString("layoutOptions", "sizing") switch
        {
            null or "block-like" => LayoutSizingMode.BlockLike,
            "manual" => LayoutSizingMode.Manual,
            var value => throw new TypeErrorException($"'{value}' is not a valid value of LayoutSizingMode"),
        };

        if (!layoutCtor.HasPrototypeObject)
            throw new TypeErrorException("The prototype of the layout class is not an object");

        var intrinsicSizes = layoutCtor.GetPrototypeFunction("intrinsicSizes");
        var layout = layoutCtor.GetPrototypeFunction("layout");
        var options = new LayoutOptions { ChildDisplay = childDisplay, Sizing = sizing };
        LayoutDefinitions[name] = new LayoutDefinition(layoutCtor, layout, intrinsicSizes, inputProperties, childInputProperties, options);

        // The document layout definition is registered by a task
        var documentDefinition = new DocumentLayoutDefinition(inputProperties, childInputProperties, options);
        QueueGlobalTask(() => LayoutWorklet.RegisterDocumentLayoutDefinition(name, documentDefinition));
    }

    /// <summary>"Filter [the properties] so that it only contains supported CSS properties and custom properties".</summary>
    private static List<string> FilterProperties(IReadOnlyList<string>? properties)
    {
        var result = new List<string>();
        foreach (var property in properties ?? [])
        {
            if (CssPropertyRegistry.IsCustomProperty(property))
            {
                result.Add(property);
            }
            else if (CssPropertyRegistry.Get(InfraAscii.ToAsciiLowercase(property)) is not null)
            {
                result.Add(InfraAscii.ToAsciiLowercase(property));
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the instance of a layout class for a box (https://drafts.css-houdini.org/css-layout-api-1/#get-a-layout-class-instance),
    /// constructed on first use, or null when its constructor throws (the constructor valid flag of the definition is then unset).
    /// </summary>
    internal object? GetLayoutClassInstance(ContainerBox box, string name, LayoutDefinition definition)
    {
        if (box.Element is not { } element)
            return null;

        var instances = _layoutClassInstances.GetOrCreateValue(element);
        var key = (name, box.PseudoElement);
        if (instances.TryGetValue(key, out var instance))
            return instance;

        if (!definition.IsConstructorValid)
            return null;

        try
        {
            instance = definition.ClassConstructor.Construct();
        }
#pragma warning disable CA1031 // An exception of the constructor is reported, and the box falls back to the flow layout
        catch (Exception ex)
#pragma warning restore CA1031
        {
            definition.IsConstructorValid = false;
            definition.ClassConstructor.ReportException(ex);
            return null;
        }

        instances[key] = instance;
        return instance;
    }

    /// <summary>
    /// Returns the <see cref="LayoutChild"/> of a box for this global scope (https://drafts.css-houdini.org/css-layout-api-1/#get-a-layout-child),
    /// usable in the given layout API context.
    /// </summary>
    internal LayoutChild GetLayoutChild(ContainerBox box, LayoutDefinition definition, LayoutApiContext context)
    {
        if (!_layoutChildren.TryGetValue(box, out var child))
        {
            child = new LayoutChild(LayoutWorklet, box, definition.ChildInputProperties);
            _layoutChildren.Add(box, child);
        }

        child.Context = context;
        return child;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/structured-data.html#structuredserializeforstorage of a value of this realm.</summary>
    internal object? SerializeData(object data) => ScriptEngine!.StructuredSerializeForStorage(this, data);

    /// <summary>https://html.spec.whatwg.org/multipage/structured-data.html#structureddeserialize in this realm.</summary>
    internal object? DeserializeData(object? serialized) => ScriptEngine!.StructuredDeserialize(this, serialized);
}

/// <summary>A layout definition (https://drafts.css-houdini.org/css-layout-api-1/#layout-definition): a class registered in a global scope.</summary>
internal sealed class LayoutDefinition(WorkletClassConstructor classConstructor, object layoutFunction, object intrinsicSizesFunction, IReadOnlyList<string> inputProperties, IReadOnlyList<string> childInputProperties, LayoutOptions options)
{
    public WorkletClassConstructor ClassConstructor { get; } = classConstructor;

    public object LayoutFunction { get; } = layoutFunction;

    public object IntrinsicSizesFunction { get; } = intrinsicSizesFunction;

    public bool IsConstructorValid { get; set; } = true;

    public IReadOnlyList<string> InputProperties { get; } = inputProperties;

    public IReadOnlyList<string> ChildInputProperties { get; } = childInputProperties;

    public LayoutOptions Options { get; } = options;
}

/// <summary>
/// A document layout definition (https://drafts.css-houdini.org/css-layout-api-1/#document-layout-definition): what the
/// document knows about a registered layout.
/// </summary>
internal sealed class DocumentLayoutDefinition(IReadOnlyList<string> inputProperties, IReadOnlyList<string> childInputProperties, LayoutOptions options)
{
    public IReadOnlyList<string> InputProperties { get; } = inputProperties;

    public IReadOnlyList<string> ChildInputProperties { get; } = childInputProperties;

    public LayoutOptions Options { get; } = options;

    /// <summary>Whether two definitions have the same input properties, child input properties and layout options.</summary>
    public bool IsEquivalent(DocumentLayoutDefinition other)
        => Options.ChildDisplay == other.Options.ChildDisplay && Options.Sizing == other.Options.Sizing
            && InputProperties.SequenceEqual(other.InputProperties, StringComparer.Ordinal) && ChildInputProperties.SequenceEqual(other.ChildInputProperties, StringComparer.Ordinal);
}

/// <summary>The modes of a <see cref="LayoutApiContext"/>.</summary>
internal enum LayoutApiMode
{
    /// <summary>The <c>layout()</c> callback runs.</summary>
    Layout,

    /// <summary>The <c>intrinsicSizes()</c> callback runs: <c>layoutNextFragment()</c> cannot be called.</summary>
    IntrinsicSizes,
}

/// <summary>
/// A layout API context (https://drafts.css-houdini.org/css-layout-api-1/#layout-api-context): the state of an invocation of
/// a callback of a layout class for a box. The object is the unique id of the invocation: the <see cref="LayoutChild"/>,
/// <see cref="LayoutFragment"/> and <see cref="FragmentResult"/> objects of another invocation are refused.
/// </summary>
internal sealed class LayoutApiContext(LayoutApiMode mode, LayoutWorkletGlobalScope globalScope, LayoutContext layoutContext, ContainerBox box, LayoutSizingMode sizing)
{
    public LayoutApiMode Mode { get; } = mode;

    public LayoutWorkletGlobalScope GlobalScope { get; } = globalScope;

    /// <summary>The layout pass that invokes the callback.</summary>
    public LayoutContext LayoutContext { get; } = layoutContext;

    /// <summary>The layout API container.</summary>
    public ContainerBox Box { get; } = box;

    public LayoutSizingMode Sizing { get; } = sizing;

    /// <summary>The writing mode of the layout API container: the sizes and the offsets of the children are relative to it.</summary>
    public WritingDirection WritingDirection { get; } = WritingDirection.FromStyle(box.Style.Layout);

    /// <summary>The work queue: the tasks of the <c>intrinsicSizes()</c> and <c>layoutNextFragment()</c> calls that are not run yet.</summary>
    public List<LayoutApiWorkTask> WorkQueue { get; } = [];

    /// <summary>The border-box inline size of the box, with the <c>block-like</c> sizing.</summary>
    public double BorderBoxInlineSize { get; init; }

    /// <summary>The border-box block size of the box when it does not depend on its contents, else null.</summary>
    public double? BorderBoxBlockSize { get; init; }

    /// <summary>The sum of the box model edges of the block axis.</summary>
    public double BlockEdges { get; init; }

    /// <summary>In the <c>intrinsicSizes()</c> callback: the block size of the content box of the box when it is definite, else null.</summary>
    public double? ContentBlockSize { get; init; }

    /// <summary>The minimum border-box block size of a box whose block size depends on its contents, or null.</summary>
    public double? MinBorderBoxBlockSize { get; init; }

    /// <summary>The maximum border-box block size of a box whose block size depends on its contents, or null.</summary>
    public double? MaxBorderBoxBlockSize { get; init; }

    /// <summary>
    /// Returns the border-box block size of the box "exactly like block containers do", given the <c>autoBlockSize</c> of the
    /// result of the layout as its intrinsic block size (https://drafts.css-houdini.org/css-layout-api-1/#construct-a-fragment-result).
    /// </summary>
    public double ComputeBorderBoxBlockSize(double autoBlockSize)
    {
        if (BorderBoxBlockSize is { } definite)
            return definite;

        var size = Math.Max(autoBlockSize, BlockEdges);
        if (MaxBorderBoxBlockSize is { } max)
        {
            size = Math.Min(size, max);
        }

        return Math.Max(size, MinBorderBoxBlockSize ?? BlockEdges);
    }
}

/// <summary>
/// A layout API work task (https://drafts.css-houdini.org/css-layout-api-1/#layout-api-work-task): a call of
/// <see cref="LayoutChild.IntrinsicSizes"/> or <see cref="LayoutChild.LayoutNextFragment"/>, whose promise is resolved when
/// the layout runs its work queue.
/// </summary>
internal sealed class LayoutApiWorkTask(LayoutChild child, LayoutConstraintsOptions? constraints)
{
    private TaskCompletionSource<LayoutFragment>? _fragmentCompletion;
    private TaskCompletionSource<LayoutIntrinsicSizes>? _intrinsicSizesCompletion;

    public LayoutChild Child { get; } = child;

    /// <summary>The constraints of a "layout" task, null for an "intrinsic-sizes" task.</summary>
    public LayoutConstraintsOptions? Constraints { get; } = constraints;

    /// <summary>The serialized data of the constraints, for a child that is a layout API container.</summary>
    public object? ConstraintsData { get; init; }

    public TaskCompletionSource<LayoutFragment> FragmentCompletion => _fragmentCompletion ??= new();

    public TaskCompletionSource<LayoutIntrinsicSizes> IntrinsicSizesCompletion => _intrinsicSizesCompletion ??= new();

    /// <summary>The result of the task, set when it is run: the promise is resolved with it once all the tasks of the queue are run.</summary>
    public object? Result { get; set; }

    /// <summary>The exception of the task, which rejects its promise.</summary>
    public Exception? Exception { get; set; }

    /// <summary>Resolves or rejects the promise of the task with its result.</summary>
    public void Complete()
    {
        if (Constraints is null)
        {
            if (Exception is not null)
            {
                IntrinsicSizesCompletion.TrySetException(Exception);
            }
            else
            {
                IntrinsicSizesCompletion.TrySetResult((LayoutIntrinsicSizes)Result!);
            }
        }
        else if (Exception is not null)
        {
            FragmentCompletion.TrySetException(Exception);
        }
        else
        {
            FragmentCompletion.TrySetResult((LayoutFragment)Result!);
        }
    }
}

/// <summary>
/// The state of a layout API container (https://drafts.css-houdini.org/css-layout-api-1/#layout-api-container) whose layout
/// is registered: a box whose display is <c>layout(name)</c>, laid out by <see cref="LayoutApiFormattingContext"/>.
/// </summary>
internal sealed class LayoutApiContainer(string name, DocumentLayoutDefinition definition)
{
    /// <summary>The name of the layout.</summary>
    public string Name { get; } = name;

    public DocumentLayoutDefinition Definition { get; } = definition;

    /// <summary>The constraint space of the layout of the box that is running (see <see cref="LayoutApiFormattingContext.Layout"/>).</summary>
    public ConstraintSpace Space { get; set; }

    /// <summary>The serialized data that the layout of the parent layout API container gives to the next layout of the box (<see cref="LayoutConstraintsOptions.Data"/>), or null.</summary>
    public object? ConstraintsData { get; set; }

    /// <summary>The serialized data of the result of the last layout of the box (<see cref="FragmentResultOptions.Data"/>), or null.</summary>
    public object? ResultData { get; set; }

    /// <summary>The layout pass in which a callback of the layout failed: the box falls back to the flow layout for the pass.</summary>
    public int FallbackPass { get; set; }

    /// <summary>The layout pass of <see cref="AutoBlockSize"/>.</summary>
    public int AutoBlockSizePass { get; set; }

    /// <summary>The border-box block size that the last layout of the box whose block size was not known ahead of time gave to the box.</summary>
    public double AutoBlockSize { get; set; }

    /// <summary>The border-box inline size of the box in the layout of <see cref="AutoBlockSize"/>.</summary>
    public double AutoBlockSizeInlineSize { get; set; }

    /// <summary>With the <c>manual</c> sizing: the result of the layout that the next layout of the box reuses (see <see cref="LayoutApiFormattingContext.Layout"/>).</summary>
    public FragmentResult? ManualResult { get; set; }

    /// <summary>With the <c>manual</c> sizing: whether <see cref="ManualResult"/> is the result of the layout of the contents (the sizes of the result are imposed to the box).</summary>
    public bool ReusesManualResult { get; set; }
}
