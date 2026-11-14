using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.Logging;

namespace Meziantou.AspNetCore.Testing.Components.Internals;

/// <summary>An interactive renderer whose output is the DOM of a WebPlatform document.</summary>
internal sealed class WebPlatformRenderer : Renderer
{
    private readonly EventLoopDispatcher _dispatcher;
    private readonly IReadOnlyDictionary<string, Func<Event, EventArgs>> _customEventArgs;
    private readonly List<Task> _pendingTasks = [];
    private readonly List<Exception> _unhandledExceptions = [];
    private readonly Dictionary<int, int> _renderCounts = [];

    public WebPlatformRenderer(IServiceProvider serviceProvider, ILoggerFactory loggerFactory, EventLoopDispatcher dispatcher, Document document, IReadOnlyDictionary<string, Func<Event, EventArgs>> customEventArgs)
        : base(serviceProvider, loggerFactory)
    {
        _dispatcher = dispatcher;
        _customEventArgs = customEventArgs;
        EventDelegator = new EventDelegator(document, DispatchDomEvent);
        Applier = new DomBatchApplier(document, EventDelegator);
    }

    public override Dispatcher Dispatcher => _dispatcher;

    public EventDelegator EventDelegator { get; }

    public DomBatchApplier Applier { get; }

    protected override RendererInfo RendererInfo { get; } = new RendererInfo("WebPlatform", isInteractive: true);

    public bool HasPendingTasks
    {
        get
        {
            _pendingTasks.RemoveAll(task => task.IsCompleted);
            return _pendingTasks.Count > 0;
        }
    }

    public Task[] GetPendingTasks()
    {
        _pendingTasks.RemoveAll(task => task.IsCompleted);
        return [.. _pendingTasks];
    }

    public int GetRenderCount(int componentId) => _renderCounts.GetValueOrDefault(componentId);

    public Exception[] TakeUnhandledExceptions()
    {
        if (_unhandledExceptions.Count == 0)
            return [];

        var result = _unhandledExceptions.ToArray();
        _unhandledExceptions.Clear();
        return result;
    }

    public new int AssignRootComponentId(IComponent component) => base.AssignRootComponentId(component);

    public IComponent CreateComponent(Type componentType) => InstantiateComponent(componentType);

    public void SetElementReferenceContext(ElementReferenceContext context) => ElementReferenceContext = context;

    public Task RenderRootAsync(int componentId, ParameterView parameters)
    {
        var task = RenderRootComponentAsync(componentId, parameters);
        TrackTask(task);
        return task;
    }

    public new void RemoveRootComponent(int componentId)
    {
        base.RemoveRootComponent(componentId);
        Applier.DetachRootComponent(componentId);
    }

    public new ArrayRange<RenderTreeFrame> GetCurrentRenderTreeFrames(int componentId) => base.GetCurrentRenderTreeFrames(componentId);

    protected override void HandleException(Exception exception) => _unhandledExceptions.Add(exception);

    protected override Task UpdateDisplayAsync(in RenderBatch renderBatch)
    {
        var updatedComponents = renderBatch.UpdatedComponents;
        for (var i = 0; i < updatedComponents.Count; i++)
        {
            var componentId = updatedComponents.Array[i].ComponentId;
            _renderCounts[componentId] = _renderCounts.GetValueOrDefault(componentId) + 1;
        }

        Applier.ApplyBatch(renderBatch);
        return Task.CompletedTask;
    }

    // The components with a render mode are interactive: they are rendered by this renderer
    protected override IComponent ResolveComponentForRenderMode(Type componentType, int? parentComponentId, IComponentActivator componentActivator, IComponentRenderMode renderMode)
        => componentActivator.CreateInstance(componentType);

    protected override void AddPendingTask(ComponentState? componentState, Task task)
    {
        TrackTask(task);
        base.AddPendingTask(componentState, task);
    }

    private void TrackTask(Task task)
    {
        if (!task.IsCompleted)
        {
            _pendingTasks.Add(task);
        }
    }

    private void DispatchDomEvent(ulong eventHandlerId, EventFieldInfo? fieldInfo, Event domEvent, Element element)
    {
        // The DOM events are dispatched synchronously by the input of the test or by the scripts: the handler runs on
        // the event loop, and its render batch is applied before the default action of the event
        using var scope = _dispatcher.Enter();
        EventArgs eventArgs;
        try
        {
            eventArgs = EventArgsFactory.Create(domEvent, GetEventArgsType(eventHandlerId), _customEventArgs);
        }
        catch (ArgumentException)
        {
            // The handler was removed by a previous handler of the same event
            return;
        }

        var task = DispatchEventAsync(eventHandlerId, fieldInfo, eventArgs);
        TrackTask(task);
    }
}
