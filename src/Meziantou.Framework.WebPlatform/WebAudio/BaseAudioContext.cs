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
    private AudioWorklet? _audioWorklet;

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

    /// <summary>Gets the listener of the spatialization of the <see cref="PannerNode"/> objects.</summary>
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

    /// <summary>Gets the worklet of the <see cref="AudioWorkletProcessor"/> classes of the context.</summary>
    /// <exception cref="InvalidOperationException">The context was created by .NET code without window: there is no script engine.</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-audioworklet"/>
    [IdlMember("audioWorklet", "https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-audioworklet")]
    public AudioWorklet AudioWorklet => _audioWorklet ??= new AudioWorklet(this, Window?.Document ?? throw new InvalidOperationException("The audio worklet of a context needs the document of a window"));

    /// <summary>The node name to parameter descriptor map (https://webaudio.github.io/web-audio-api/#node-name-to-parameter-descriptor-map).</summary>
    internal Dictionary<string, IReadOnlyList<AudioParamDescriptor>> ProcessorParameterDescriptors { get; } = new(StringComparer.Ordinal);

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

    /// <summary>Creates a node that provides the time-domain and frequency-domain data of its input.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createanalyser"/>
    [IdlMember("createAnalyser", "https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createanalyser")]
    public AnalyserNode CreateAnalyser() => new(this);

    /// <summary>Creates a second-order filter.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createbiquadfilter"/>
    [IdlMember("createBiquadFilter", "https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createbiquadfilter")]
    public BiquadFilterNode CreateBiquadFilter() => new(this);

    /// <summary>Creates a buffer of silence.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createbuffer"/>
    [IdlMember("createBuffer", "https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createbuffer")]
    public AudioBuffer CreateBuffer(uint numberOfChannels, uint length, float sampleRate) => new(numberOfChannels, length, sampleRate);

    /// <summary>Creates a node that plays an <see cref="AudioBuffer"/>.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createbuffersource"/>
    [IdlMember("createBufferSource", "https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createbuffersource")]
    public AudioBufferSourceNode CreateBufferSource() => new(this);

    /// <summary>Creates a node that combines its inputs into the channels of its output.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createchannelmerger"/>
    [IdlMember("createChannelMerger", "https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createchannelmerger")]
    public ChannelMergerNode CreateChannelMerger(uint numberOfInputs = 6) => new(this, new ChannelMergerOptions { NumberOfInputs = numberOfInputs });

    /// <summary>Creates a node whose outputs are the channels of its input.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createchannelsplitter"/>
    [IdlMember("createChannelSplitter", "https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createchannelsplitter")]
    public ChannelSplitterNode CreateChannelSplitter(uint numberOfOutputs = 6) => new(this, new ChannelSplitterOptions { NumberOfOutputs = numberOfOutputs });

    /// <summary>Creates a node that outputs a constant value.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createconstantsource"/>
    [IdlMember("createConstantSource", "https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createconstantsource")]
    public ConstantSourceNode CreateConstantSource() => new(this);

    /// <summary>Creates a node that convolves its input with an impulse response.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createconvolver"/>
    [IdlMember("createConvolver", "https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createconvolver")]
    public ConvolverNode CreateConvolver() => new(this);

    /// <summary>Creates a node that delays its input.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createdelay"/>
    [IdlMember("createDelay", "https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createdelay")]
    public DelayNode CreateDelay(double maxDelayTime = 1) => new(this, new DelayOptions { MaxDelayTime = maxDelayTime });

    /// <summary>Creates a dynamics compressor.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createdynamicscompressor"/>
    [IdlMember("createDynamicsCompressor", "https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createdynamicscompressor")]
    public DynamicsCompressorNode CreateDynamicsCompressor() => new(this);

    /// <summary>Creates a node that multiplies its input by a gain.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-creategain"/>
    [IdlMember("createGain", "https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-creategain")]
    public GainNode CreateGain() => new(this);

    /// <summary>Creates a general infinite impulse response filter.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createiirfilter"/>
    [IdlMember("createIIRFilter", "https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createiirfilter")]
    public IIRFilterNode CreateIIRFilter(IReadOnlyList<double> feedforward, IReadOnlyList<double> feedback) => new(this, new IIRFilterOptions { Feedforward = feedforward, Feedback = feedback });

    /// <summary>Creates a source of a periodic waveform.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createoscillator"/>
    [IdlMember("createOscillator", "https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createoscillator")]
    public OscillatorNode CreateOscillator() => new(this);

    /// <summary>Creates a node that spatializes its input relative to the listener.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createpanner"/>
    [IdlMember("createPanner", "https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createpanner")]
    public PannerNode CreatePanner() => new(this);

    /// <summary>Creates a periodic waveform from the coefficients of its Fourier series.</summary>
    /// <exception cref="DomException">The arrays have different lengths, or a length less than 2 (an <c>IndexSizeError</c>).</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createperiodicwave"/>
    [IdlMember("createPeriodicWave", "https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createperiodicwave")]
    public PeriodicWave CreatePeriodicWave(IReadOnlyList<float> real, IReadOnlyList<float> imag, PeriodicWaveConstraints? constraints = null)
    {
        ArgumentNullException.ThrowIfNull(real);
        ArgumentNullException.ThrowIfNull(imag);
        if (real.Count != imag.Count)
            throw new DomException($"The real ({real.Count}) and imaginary ({imag.Count}) coefficients have different lengths", "IndexSizeError");

        return new PeriodicWave(this, new PeriodicWaveOptions { Real = real, Imag = imag, DisableNormalization = constraints?.DisableNormalization ?? false });
    }

    /// <summary>Creates a node that processes its input with the <c>audioprocess</c> events (deprecated).</summary>
    /// <param name="bufferSize">The number of frames of the buffers of the events (a power of two from 256 to 16384), or 0 to let the implementation choose.</param>
    /// <param name="numberOfInputChannels">The number of channels of the input.</param>
    /// <param name="numberOfOutputChannels">The number of channels of the output.</param>
    /// <exception cref="DomException">An argument is out of its range (an <c>IndexSizeError</c>), or the buffer size is not a multiple of the render quantum size (a <c>NotSupportedError</c>).</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createscriptprocessor"/>
    [IdlMember("createScriptProcessor", "https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createscriptprocessor")]
    public ScriptProcessorNode CreateScriptProcessor(uint bufferSize = 0, uint numberOfInputChannels = 2, uint numberOfOutputChannels = 2)
    {
        var size = ScriptProcessorNode.ValidateArguments(this, bufferSize, numberOfInputChannels, numberOfOutputChannels);
        return new ScriptProcessorNode(this, size, (int)numberOfInputChannels, (int)numberOfOutputChannels);
    }

    /// <summary>Creates a node that pans its input in a stereo image.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createstereopanner"/>
    [IdlMember("createStereoPanner", "https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createstereopanner")]
    public StereoPannerNode CreateStereoPanner() => new(this);

    /// <summary>Creates a node that applies a distortion curve to its input.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createwaveshaper"/>
    [IdlMember("createWaveShaper", "https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createwaveshaper")]
    public WaveShaperNode CreateWaveShaper() => new(this);

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
