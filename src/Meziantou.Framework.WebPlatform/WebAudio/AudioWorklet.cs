using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.WebAudio;

/// <summary>The description of a parameter of an <see cref="AudioWorkletProcessor"/> (https://webaudio.github.io/web-audio-api/#AudioParamDescriptor).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#AudioParamDescriptor"/>
[IdlInterface("AudioParamDescriptor", "https://webaudio.github.io/web-audio-api/#AudioParamDescriptor")]
public sealed class AudioParamDescriptor
{
    /// <summary>Gets the name of the parameter.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioparamdescriptor-name"/>
    [IdlMember("name", "https://webaudio.github.io/web-audio-api/#dom-audioparamdescriptor-name")]
    public required string Name { get; init; }

    /// <summary>Gets the default value.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioparamdescriptor-defaultvalue"/>
    [IdlMember("defaultValue", "https://webaudio.github.io/web-audio-api/#dom-audioparamdescriptor-defaultvalue")]
    public float DefaultValue { get; init; }

    /// <summary>Gets the minimum value.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioparamdescriptor-minvalue"/>
    [IdlMember("minValue", "https://webaudio.github.io/web-audio-api/#dom-audioparamdescriptor-minvalue")]
    public float MinValue { get; init; } = -float.MaxValue;

    /// <summary>Gets the maximum value.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioparamdescriptor-maxvalue"/>
    [IdlMember("maxValue", "https://webaudio.github.io/web-audio-api/#dom-audioparamdescriptor-maxvalue")]
    public float MaxValue { get; init; } = float.MaxValue;

    /// <summary>Gets the automation rate.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioparamdescriptor-automationrate"/>
    [IdlMember("automationRate", "https://webaudio.github.io/web-audio-api/#dom-audioparamdescriptor-automationrate")]
    public AutomationRate AutomationRate { get; init; }
}

/// <summary>
/// The worklet of the <see cref="AudioWorkletProcessor"/> classes of a context (https://webaudio.github.io/web-audio-api/#AudioWorklet):
/// its module scripts register the processors of the <see cref="AudioWorkletNode"/> objects.
/// </summary>
/// <remarks>
/// The global scope of the worklet is a realm of the script engine of the document of the context: the processors run on
/// the thread of the event loop of the document, during the rendering of the context.
/// </remarks>
/// <seealso href="https://webaudio.github.io/web-audio-api/#AudioWorklet"/>
[IdlInterface("AudioWorklet", "https://webaudio.github.io/web-audio-api/#AudioWorklet")]
public sealed class AudioWorklet : Worklet
{
    private readonly MessagePort _globalScopePort;

    internal AudioWorklet(BaseAudioContext context, Document document)
        : base(document)
    {
        Context = context;
        var channel = new MessageChannel();
        Port = channel.Port1;
        _globalScopePort = channel.Port2;
        document.RegisterAudioWorklet(this);
    }

    /// <summary>The context of the worklet.</summary>
    internal BaseAudioContext Context { get; }

    /// <summary>Gets the port connected to the port of the global scope of the worklet.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioworklet-port"/>
    [IdlMember("port", "https://webaudio.github.io/web-audio-api/#dom-audioworklet-port")]
    public MessagePort Port { get; }

    internal override RequestDestination Destination => RequestDestination.AudioWorklet;

    /// <summary>The global scope of the worklet, created by the first call of <see cref="Worklet.AddModule"/>.</summary>
    internal AudioWorkletGlobalScope? GlobalScope => GlobalScopes.Count > 0 ? (AudioWorkletGlobalScope)GlobalScopes[0] : null;

    private protected override WorkletGlobalScope CreateGlobalScopeCore() => new AudioWorkletGlobalScope(this, _globalScopePort);
}

/// <summary>
/// The global scope of an <see cref="AudioWorklet"/> (https://webaudio.github.io/web-audio-api/#AudioWorkletGlobalScope): the
/// module scripts register the <see cref="AudioWorkletProcessor"/> classes with <see cref="RegisterProcessor"/>.
/// </summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#AudioWorkletGlobalScope"/>
[IdlInterface("AudioWorkletGlobalScope", "https://webaudio.github.io/web-audio-api/#AudioWorkletGlobalScope")]
public sealed class AudioWorkletGlobalScope : WorkletGlobalScope
{
    // https://webaudio.github.io/web-audio-api/#pending-processor-construction-data: the node and the port of the processor
    // being constructed (the processors are constructed on the thread of the event loop)
    [ThreadStatic]
    private static PendingProcessorConstruction? s_pendingConstruction;

    internal AudioWorkletGlobalScope(AudioWorklet worklet, MessagePort port)
        : base(worklet)
    {
        AudioWorklet = worklet;
        Port = port;
        port.SetRelevantGlobal(this);
    }

    internal AudioWorklet AudioWorklet { get; }

    internal BaseAudioContext Context => AudioWorklet.Context;

    internal override ExposureSet Exposure => ExposureSet.AudioWorklet;

    internal override string InterfaceName => "AudioWorkletGlobalScope";

    /// <summary>The node name to processor constructor map (https://webaudio.github.io/web-audio-api/#node-name-to-processor-constructor-map).</summary>
    internal Dictionary<string, WorkletClassConstructor> ProcessorConstructors { get; } = new(StringComparer.Ordinal);

    /// <summary>The frame of the render quantum being processed (the current frame of the context outside of the rendering).</summary>
    internal long? RenderingFrame { get; set; }

    /// <summary>Gets the frame of the render quantum being processed.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioworkletglobalscope-currentframe"/>
    [IdlMember("currentFrame", "https://webaudio.github.io/web-audio-api/#dom-audioworkletglobalscope-currentframe")]
    public ulong CurrentFrame => (ulong)(RenderingFrame ?? Context.CurrentFrame);

    /// <summary>Gets the time of the render quantum being processed.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioworkletglobalscope-currenttime"/>
    [IdlMember("currentTime", "https://webaudio.github.io/web-audio-api/#dom-audioworkletglobalscope-currenttime")]
    public double CurrentTime => (RenderingFrame ?? Context.CurrentFrame) / (double)Context.SampleRate;

    /// <summary>Gets the sample rate of the context.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioworkletglobalscope-samplerate"/>
    [IdlMember("sampleRate", "https://webaudio.github.io/web-audio-api/#dom-audioworkletglobalscope-samplerate")]
    public float SampleRate => Context.SampleRate;

    /// <summary>Gets the render quantum size of the context.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioworkletglobalscope-renderquantumsize"/>
    [IdlMember("renderQuantumSize", "https://webaudio.github.io/web-audio-api/#dom-audioworkletglobalscope-renderquantumsize")]
    public uint RenderQuantumSize => Context.RenderQuantumSize;

    /// <summary>Gets the port connected to the port of the <see cref="AudioWorklet"/>.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioworkletglobalscope-port"/>
    [IdlMember("port", "https://webaudio.github.io/web-audio-api/#dom-audioworkletglobalscope-port")]
    public MessagePort Port { get; }

    /// <summary>Registers a class derived from <see cref="AudioWorkletProcessor"/> under a name.</summary>
    /// <exception cref="DomException">The name is empty or already registered, or a parameter descriptor is not valid (a <c>NotSupportedError</c> or an <c>InvalidStateError</c>).</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioworkletglobalscope-registerprocessor"/>
    [IdlMember("registerProcessor", "https://webaudio.github.io/web-audio-api/#dom-audioworkletglobalscope-registerprocessor")]
    public void RegisterProcessor(string name, WorkletClassConstructor processorCtor)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(processorCtor);
        if (name.Length == 0)
            throw new DomException("The name of a processor cannot be empty", "NotSupportedError");

        if (ProcessorConstructors.ContainsKey(name))
            throw new DomException($"The processor '{name}' is already registered", "NotSupportedError");

        if (!processorCtor.IsConstructor)
            throw new TypeErrorException("The processor class is not a constructor");

        if (!processorCtor.HasPrototypeObject)
            throw new TypeErrorException("The prototype of the processor class is not an object");

        var descriptors = processorCtor.GetAudioParamDescriptors() ?? [];
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var descriptor in descriptors)
        {
            if (!names.Add(descriptor.Name))
                throw new DomException($"The parameter '{descriptor.Name}' is described twice", "NotSupportedError");

            if (!(descriptor.MinValue <= descriptor.DefaultValue && descriptor.DefaultValue <= descriptor.MaxValue))
                throw new DomException($"The default value of the parameter '{descriptor.Name}' is not between its minimum and maximum values", "InvalidStateError");
        }

        ProcessorConstructors[name] = processorCtor;

        // The node name to parameter descriptor map of the context is updated by a task (before the promise of addModule() is resolved)
        Worklet.Document.QueueGlobalTask(() => Context.ProcessorParameterDescriptors[name] = descriptors);
    }

    /// <summary>Takes the pending processor construction data (https://webaudio.github.io/web-audio-api/#pending-processor-construction-data).</summary>
    internal static PendingProcessorConstruction? TakePendingConstruction()
    {
        var pending = s_pendingConstruction;
        s_pendingConstruction = null;
        return pending;
    }

    /// <summary>Sets the pending processor construction data while the constructor of a processor runs.</summary>
    internal static void SetPendingConstruction(PendingProcessorConstruction? pending) => s_pendingConstruction = pending;
}

/// <summary>The node and the port of a processor being constructed.</summary>
internal sealed record PendingProcessorConstruction(AudioWorkletNode Node, MessagePort Port);

/// <summary>
/// The base class of the audio processors of the scripts (https://webaudio.github.io/web-audio-api/#AudioWorkletProcessor):
/// a processor is constructed in the <see cref="AudioWorkletGlobalScope"/> for each <see cref="AudioWorkletNode"/>, and its
/// <c>process()</c> method is called for each render quantum.
/// </summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#AudioWorkletProcessor"/>
[IdlInterface("AudioWorkletProcessor", "https://webaudio.github.io/web-audio-api/#AudioWorkletProcessor")]
public class AudioWorkletProcessor
{
    /// <summary>Creates a processor: only the construction of an <see cref="AudioWorkletNode"/> constructs a processor.</summary>
    /// <exception cref="TypeErrorException">No node is being constructed.</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioworkletprocessor-audioworkletprocessor"/>
    [IdlMember("constructor", "https://webaudio.github.io/web-audio-api/#dom-audioworkletprocessor-audioworkletprocessor")]
    public AudioWorkletProcessor()
    {
        var pending = AudioWorkletGlobalScope.TakePendingConstruction() ?? throw new TypeErrorException("An AudioWorkletProcessor can only be constructed by an AudioWorkletNode");
        Node = pending.Node;
        Port = pending.Port;
    }

    /// <summary>The node of the processor ([[node reference]]).</summary>
    internal AudioWorkletNode Node { get; }

    /// <summary>Gets the port connected to the port of the <see cref="AudioWorkletNode"/>.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioworkletprocessor-port"/>
    [IdlMember("port", "https://webaudio.github.io/web-audio-api/#dom-audioworkletprocessor-port")]
    public MessagePort Port { get; }
}
