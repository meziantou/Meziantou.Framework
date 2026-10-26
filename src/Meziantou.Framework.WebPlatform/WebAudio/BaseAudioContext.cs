using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.WebAudio.Rendering;

namespace Meziantou.Framework.WebPlatform.WebAudio;

/// <summary>
/// The base of the audio contexts (https://webaudio.github.io/web-audio-api/#BaseAudioContext): a graph of
/// <see cref="AudioNode"/> objects rendered in blocks of sample frames (render quanta) to its
/// <see cref="Destination"/>, with a time that starts at 0 and advances as the graph is rendered.
/// </summary>
/// <remarks>
/// The graph is rendered on the thread of the event loop of the window of the context, never concurrently with its
/// scripts. A context created by .NET code without window renders when its methods are called.
/// </remarks>
/// <seealso href="https://webaudio.github.io/web-audio-api/#BaseAudioContext"/>
[IdlInterface("BaseAudioContext", "https://webaudio.github.io/web-audio-api/#BaseAudioContext")]
public abstract class BaseAudioContext : EventTarget
{
    /// <summary>The default render quantum size (https://webaudio.github.io/web-audio-api/#render-quantum-size).</summary>
    internal const int DefaultRenderQuantumSize = 128;

    private readonly List<AudioNode> _nodes = [];
    private bool _graphChanged = true;
    private AudioNode[] _order = [];
    private AudioNode[] _splitNodes = [];
    private AudioContextState _state = AudioContextState.Suspended;

    private protected BaseAudioContext(float sampleRate, int renderQuantumSize, uint destinationChannelCount, uint maxChannelCount, bool isOffline)
    {
        Window = GlobalScope.Current as Window;
        SampleRate = sampleRate;
        RenderQuantumSizeInternal = renderQuantumSize;
        Destination = new AudioDestinationNode(this, destinationChannelCount, maxChannelCount, isOffline);
        Listener = new AudioListener(this);
    }

    /// <summary>The window of the context (the relevant global object), or null for a context created by .NET code.</summary>
    internal Window? Window { get; }

    /// <summary>The number of sample frames of a render quantum.</summary>
    internal int RenderQuantumSizeInternal { get; }

    /// <summary>The number of sample frames rendered so far.</summary>
    internal long CurrentFrame { get; private protected set; }

    /// <summary>Gets the node to which the audio of the graph is rendered.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-destination"/>
    [IdlMember("destination", "https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-destination")]
    public AudioDestinationNode Destination { get; }

    /// <summary>Gets the sample rate of the context in Hz.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-samplerate"/>
    [IdlMember("sampleRate", "https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-samplerate")]
    public float SampleRate { get; }

    /// <summary>Gets the time of the context in seconds: the time of the next sample frame to render.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-currenttime"/>
    [IdlMember("currentTime", "https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-currenttime")]
    public double CurrentTime => CurrentFrame / (double)SampleRate;

    /// <summary>Gets the listener of the spatialization of the <c>PannerNode</c> objects.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-listener"/>
    [IdlMember("listener", "https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-listener")]
    public AudioListener Listener { get; }

    /// <summary>Gets the state of the context.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-state"/>
    [IdlMember("state", "https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-state")]
    public AudioContextState State => _state;

    /// <summary>Gets the number of sample frames of a render quantum.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-renderquantumsize"/>
    [IdlMember("renderQuantumSize", "https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-renderquantumsize")]
    public uint RenderQuantumSize => (uint)RenderQuantumSizeInternal;

    /// <summary>Gets or sets the event handler of the <c>statechange</c> event.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-onstatechange"/>
    [IdlMember("onstatechange", "https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-onstatechange")]
    public Action<Event>? OnStateChange
    {
        get => GetEventHandler("statechange");
        set => SetEventHandler("statechange", value);
    }

    internal override GlobalScope? RelevantGlobal => Window ?? base.RelevantGlobal;

    /// <summary>Whether the document of the window of the context is fully active (always true without window).</summary>
    internal bool IsDocumentFullyActive => Window is not { } window || window.Document.IsFullyActive;

    /// <summary>Creates a buffer of silence.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createbuffer"/>
    [IdlMember("createBuffer", "https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createbuffer")]
    public AudioBuffer CreateBuffer(uint numberOfChannels, uint length, float sampleRate) => new(numberOfChannels, length, sampleRate);

    /// <summary>Creates a node that plays an <see cref="AudioBuffer"/>.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createbuffersource"/>
    [IdlMember("createBufferSource", "https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createbuffersource")]
    public AudioBufferSourceNode CreateBufferSource() => new(this);

    /// <summary>Creates a node that outputs a constant value.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createconstantsource"/>
    [IdlMember("createConstantSource", "https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createconstantsource")]
    public ConstantSourceNode CreateConstantSource() => new(this);

    /// <summary>Creates a node that multiplies its input by a gain.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-creategain"/>
    [IdlMember("createGain", "https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-creategain")]
    public GainNode CreateGain() => new(this);

    // ----------------------------------------------------------------------------------------------------------------
    // State
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Sets the state of the context and queues a task that fires <c>statechange</c>
    /// (https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-onstatechange).
    /// </summary>
    private protected void SetState(AudioContextState state)
    {
        if (_state == state)
            return;

        _state = state;
        QueueMediaElementTask(() => FireEvent(new Event("statechange")));
    }

    /// <summary>Sets the state without firing <c>statechange</c> (e.g. from a task that fires it itself).</summary>
    private protected void SetStateSilently(AudioContextState state) => _state = state;

    /// <summary>Queues a media element task (https://html.spec.whatwg.org/multipage/media.html#queue-a-media-element-task) on the event loop of the window, or runs it at once without window.</summary>
    internal void QueueMediaElementTask(Action task)
    {
        if (Window is { } window)
        {
            window.QueueGlobalTask(task);
        }
        else
        {
            task();
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Rendering
    // ----------------------------------------------------------------------------------------------------------------

    internal void RegisterNode(AudioNode node)
    {
        _nodes.Add(node);
        _graphChanged = true;
    }

    /// <summary>Called when a connection is added or removed, or when a node starts or stops requiring the rendering.</summary>
    internal void OnGraphChanged() => _graphChanged = true;

    /// <summary>
    /// Renders a render quantum of the graph (https://webaudio.github.io/web-audio-api/#rendering-loop): the nodes are
    /// processed in dependency order, and the time of the context advances. Returns the output of the destination.
    /// </summary>
    internal AudioBus RenderQuantum()
    {
        if (_graphChanged)
        {
            UpdateOrder();
        }

        var startFrame = CurrentFrame;
        var length = RenderQuantumSizeInternal;
        foreach (var node in _order)
        {
            node.RenderQuantum(startFrame, length);
        }

        foreach (var node in _splitNodes)
        {
            node.RenderCycleInput(startFrame, length);
        }

        CurrentFrame = startFrame + length;
        foreach (var node in _order)
        {
            if (node is AudioScheduledSourceNode source)
            {
                source.CheckEnded(CurrentFrame);
            }
        }

        OnQuantumRendered();
        return Destination.GetOutputBus(0);
    }

    /// <summary>Called when a source node is started (an <c>AudioContext</c> that is allowed to start then starts rendering).</summary>
    internal virtual void OnSourceStarted()
    {
    }

    /// <summary>Called after each render quantum (e.g. to fire the ended events of the sources).</summary>
    private protected virtual void OnQuantumRendered()
    {
    }

    private void UpdateOrder()
    {
        _graphChanged = false;

        // The nodes without connections are skipped (they output silence), except those that must run (e.g. a started source)
        var nodes = new List<AudioNode>(_nodes.Count);
        foreach (var node in _nodes)
        {
            if (node == Destination || node.HasConnections || node.RequiresProcessing)
            {
                nodes.Add(node);
            }
        }

        (_order, _splitNodes) = AudioGraph.ComputeOrder(nodes);
    }
}
