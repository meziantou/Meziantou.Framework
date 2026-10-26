using System.Collections;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.WebAudio.Rendering;

namespace Meziantou.Framework.WebPlatform.WebAudio;

/// <summary>The options of an <see cref="AudioWorkletNode"/> (https://webaudio.github.io/web-audio-api/#AudioWorkletNodeOptions).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#AudioWorkletNodeOptions"/>
[IdlInterface("AudioWorkletNodeOptions", "https://webaudio.github.io/web-audio-api/#AudioWorkletNodeOptions")]
public sealed class AudioWorkletNodeOptions : AudioNodeOptions
{
    /// <summary>Gets the number of inputs.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioworkletnodeoptions-numberofinputs"/>
    [IdlMember("numberOfInputs", "https://webaudio.github.io/web-audio-api/#dom-audioworkletnodeoptions-numberofinputs")]
    public uint NumberOfInputs { get; init; } = 1;

    /// <summary>Gets the number of outputs.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioworkletnodeoptions-numberofoutputs"/>
    [IdlMember("numberOfOutputs", "https://webaudio.github.io/web-audio-api/#dom-audioworkletnodeoptions-numberofoutputs")]
    public uint NumberOfOutputs { get; init; } = 1;

    /// <summary>Gets the number of channels of each output, or null.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioworkletnodeoptions-outputchannelcount"/>
    [IdlMember("outputChannelCount", "https://webaudio.github.io/web-audio-api/#dom-audioworkletnodeoptions-outputchannelcount")]
    public IReadOnlyList<uint>? OutputChannelCount { get; init; }

    /// <summary>Gets the initial values of the parameters, by name.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioworkletnodeoptions-parameterdata"/>
    [IdlMember("parameterData", "https://webaudio.github.io/web-audio-api/#dom-audioworkletnodeoptions-parameterdata")]
    public IReadOnlyList<KeyValuePair<string, double>>? ParameterData { get; init; }

    /// <summary>Gets the options given to the constructor of the processor (a value of the script engine, structured-cloned in the global scope of the worklet).</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioworkletnodeoptions-processoroptions"/>
    [IdlMember("processorOptions", "https://webaudio.github.io/web-audio-api/#dom-audioworkletnodeoptions-processoroptions")]
    public object? ProcessorOptions { get; init; }
}

/// <summary>The parameters of an <see cref="AudioWorkletNode"/>, by name (https://webaudio.github.io/web-audio-api/#AudioParamMap): a read-only map.</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#AudioParamMap"/>
[IdlInterface("AudioParamMap", "https://webaudio.github.io/web-audio-api/#AudioParamMap")]
public sealed class AudioParamMap : IReadOnlyCollection<KeyValuePair<string, AudioParam>>
{
    private readonly List<KeyValuePair<string, AudioParam>> _entries;

    internal AudioParamMap(List<KeyValuePair<string, AudioParam>> entries)
    {
        _entries = entries;
    }

    /// <summary>Gets the number of parameters.</summary>
    [NotInIdl("The size member of maplike<DOMString, AudioParam>")]
    public int Count => _entries.Count;

    /// <summary>Returns the parameter of a name, or null.</summary>
    [NotInIdl("The get() member of maplike<DOMString, AudioParam>")]
    public AudioParam? Get(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        foreach (var (key, value) in _entries)
        {
            if (string.Equals(key, name, StringComparison.Ordinal))
                return value;
        }

        return null;
    }

    /// <summary>Returns whether there is a parameter of a name.</summary>
    [NotInIdl("The has() member of maplike<DOMString, AudioParam>")]
    public bool Has(string name) => Get(name) is not null;

    /// <inheritdoc/>
    [NotInIdl("The iteration of maplike<DOMString, AudioParam>")]
    public IEnumerator<KeyValuePair<string, AudioParam>> GetEnumerator() => _entries.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// A node whose processing is implemented by a script (https://webaudio.github.io/web-audio-api/#AudioWorkletNode): its
/// <see cref="AudioWorkletProcessor"/>, registered in the <see cref="BaseAudioContext.AudioWorklet"/> of its context.
/// </summary>
/// <remarks>
/// The processor is constructed by a task queued by the constructor of the node, and its <c>process()</c> method is called
/// during the rendering of each render quantum while the node is actively processing, on the thread of the event loop.
/// </remarks>
/// <seealso href="https://webaudio.github.io/web-audio-api/#AudioWorkletNode"/>
[IdlInterface("AudioWorkletNode", "https://webaudio.github.io/web-audio-api/#AudioWorkletNode")]
public sealed class AudioWorkletNode : AudioNode
{
    private readonly string _name;
    private readonly int[]? _outputChannelCounts;
    private readonly AudioParam[] _parameters;
    private readonly string[] _parameterNames;
    private readonly MessagePort _processorPort;

    // The processor: its object of the script engine, and the call site of its process() method
    private object? _processorObject;
    private AudioWorkletProcessCall? _processCall;

    // [[callable process]] and the active source flag (https://webaudio.github.io/web-audio-api/#active-source)
    private bool _callableProcess = true;
    private bool _activeSource = true;

    /// <summary>Creates a node of a processor registered in the audio worklet of the context.</summary>
    /// <exception cref="DomException">
    /// The processor is not registered (an <c>InvalidStateError</c>), the numbers of inputs and outputs are both 0 or an output
    /// channel count is not supported (a <c>NotSupportedError</c>), or the output channel counts do not match the outputs (an
    /// <c>IndexSizeError</c>).
    /// </exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioworkletnode-audioworkletnode"/>
    [IdlMember("constructor", "https://webaudio.github.io/web-audio-api/#dom-audioworkletnode-audioworkletnode")]
    public AudioWorkletNode(BaseAudioContext context, string name, AudioWorkletNodeOptions? options = null)
        : base(context, Validate(context, name, options ??= new AudioWorkletNodeOptions()), (int)options.NumberOfOutputs, options.ChannelCount ?? 2, options.ChannelCountMode ?? ChannelCountMode.Max, options.ChannelInterpretation ?? ChannelInterpretation.Speakers)
    {
        _name = name;
        ApplyOptions(options);
        if (options.OutputChannelCount is { } outputChannelCount)
        {
            _outputChannelCounts = [.. outputChannelCount.Select(c => (int)c)];
        }
        else if (options.NumberOfInputs != 1 || options.NumberOfOutputs != 1)
        {
            _outputChannelCounts = [.. Enumerable.Repeat(1, (int)options.NumberOfOutputs)];
        }

        // https://webaudio.github.io/web-audio-api/#dom-audioworkletnode-audioworkletnode: the parameters of the descriptors
        var descriptors = context.ProcessorParameterDescriptors[name];
        var entries = new List<KeyValuePair<string, AudioParam>>(descriptors.Count);
        foreach (var descriptor in descriptors)
        {
            var parameter = new AudioParam(context, this, "AudioWorkletNode." + descriptor.Name, descriptor.DefaultValue, descriptor.MinValue, descriptor.MaxValue, descriptor.AutomationRate);
            entries.Add(new(descriptor.Name, parameter));
        }

        if (options.ParameterData is { } parameterData)
        {
            foreach (var (parameterName, value) in parameterData)
            {
                foreach (var (key, parameter) in entries)
                {
                    if (string.Equals(key, parameterName, StringComparison.Ordinal))
                    {
                        parameter.Value = (float)value;
                    }
                }
            }
        }

        Parameters = new AudioParamMap(entries);
        _parameters = [.. entries.Select(e => e.Value)];
        _parameterNames = [.. entries.Select(e => e.Key)];

        var channel = new MessageChannel();
        Port = channel.Port1;
        _processorPort = channel.Port2;
        var globalScope = context.AudioWorklet.GlobalScope!;
        _processorPort.SetRelevantGlobal(globalScope);

        // The options are structured-cloned to the global scope of the worklet, for the constructor of the processor
        var serializedProcessorOptions = options.ProcessorOptions is { } processorOptions && context.Window is { ScriptEngine: { } engine } window
            ? engine.StructuredSerializeForAgent(window, processorOptions, [])
            : null;
        var hasProcessorOptions = options.ProcessorOptions is not null;
        var constructionOptions = options;

        // https://webaudio.github.io/web-audio-api/#instantiation-of-AudioWorkletProcessor: the processor is constructed by a task
        context.QueueMediaElementTask(() => ConstructProcessor(globalScope, constructionOptions, hasProcessorOptions, serializedProcessorOptions));
    }

    private static int Validate(BaseAudioContext context, string name, AudioWorkletNodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(name);
        if (!context.ProcessorParameterDescriptors.ContainsKey(name))
            throw new DomException($"The processor '{name}' is not registered in the audio worklet of the context", "InvalidStateError");

        // https://webaudio.github.io/web-audio-api/#configuring-channels-with-audioworkletnodeoptions
        if (options.NumberOfInputs == 0 && options.NumberOfOutputs == 0)
            throw new DomException("The numbers of inputs and outputs cannot both be 0", "NotSupportedError");

        if (options.NumberOfInputs > MaxChannels || options.NumberOfOutputs > MaxChannels)
            throw new DomException($"The numbers of inputs and outputs cannot be greater than {MaxChannels}", "IndexSizeError");

        if (options.OutputChannelCount is { } outputChannelCount)
        {
            if (outputChannelCount.Any(c => c is 0 or > MaxChannels))
                throw new DomException($"An output channel count is not between 1 and {MaxChannels}", "NotSupportedError");

            if (outputChannelCount.Count != options.NumberOfOutputs)
                throw new DomException($"The {outputChannelCount.Count} output channel counts do not match the {options.NumberOfOutputs} outputs", "IndexSizeError");
        }

        return (int)options.NumberOfInputs;
    }

    /// <summary>Gets the parameters of the processor.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioworkletnode-parameters"/>
    [IdlMember("parameters", "https://webaudio.github.io/web-audio-api/#dom-audioworkletnode-parameters")]
    public AudioParamMap Parameters { get; }

    /// <summary>Gets the port connected to the port of the processor.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioworkletnode-port"/>
    [IdlMember("port", "https://webaudio.github.io/web-audio-api/#dom-audioworkletnode-port")]
    public MessagePort Port { get; }

    /// <summary>Gets or sets the event handler of the <c>processorerror</c> event.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioworkletnode-onprocessorerror"/>
    [IdlMember("onprocessorerror", "https://webaudio.github.io/web-audio-api/#dom-audioworkletnode-onprocessorerror")]
    public Action<Event>? OnProcessorError
    {
        get => GetEventHandler("processorerror");
        set => SetEventHandler("processorerror", value);
    }

    private protected override IEnumerable<AudioParam> NodeParameters => _parameters;

    // The active source flag keeps the node processing without connected inputs
    internal override bool RequiresProcessing => _processorObject is not null && _callableProcess && _activeSource;

    private protected override bool IsSourceActive(long startFrame, int length) => _processorObject is not null && _callableProcess && _activeSource;

    /// <summary>Constructs the processor in the global scope of the worklet (https://webaudio.github.io/web-audio-api/#instantiation-of-AudioWorkletProcessor).</summary>
    private void ConstructProcessor(AudioWorkletGlobalScope globalScope, AudioWorkletNodeOptions options, bool hasProcessorOptions, object? serializedProcessorOptions)
    {
        if (globalScope.IsTerminated || !globalScope.ProcessorConstructors.TryGetValue(_name, out var constructor))
            return;

        var engine = globalScope.ScriptEngine;
        try
        {
            object? processorOptions = null;
            if (hasProcessorOptions && engine is not null)
            {
                processorOptions = engine.StructuredDeserialize(globalScope, serializedProcessorOptions);
            }

            var optionsObject = constructor.CreateObject(CreateOptionsProperties(options, hasProcessorOptions, processorOptions));
            AudioWorkletGlobalScope.SetPendingConstruction(new PendingProcessorConstruction(this, _processorPort));
            object instance;
            try
            {
                instance = constructor.Construct([optionsObject]);
            }
            finally
            {
                AudioWorkletGlobalScope.SetPendingConstruction(null);
            }

            // The constructor must return the object of a processor of this node
            if (constructor.AsPlatformObject(instance) is not AudioWorkletProcessor { } processor || processor.Node != this)
                throw new DomException("The constructor of the processor did not return the processor of the node", "InvalidStateError");

            _processorObject = instance;
            _processCall = constructor.CreateProcessCall(instance);
            Context.OnGraphChanged();
        }
#pragma warning disable CA1031 // An exception of the constructor of the processor is reported to the node
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _callableProcess = false;
            ReportProcessorError(globalScope, ex);
        }
    }

    private static List<KeyValuePair<string, object?>> CreateOptionsProperties(AudioWorkletNodeOptions options, bool hasProcessorOptions, object? processorOptions)
    {
        // The members of the dictionary converted to an object (https://webidl.spec.whatwg.org/#es-dictionary): the members
        // with a default value, and the members that were given
        var properties = new List<KeyValuePair<string, object?>>();
        if (options.ChannelCount is { } channelCount)
        {
            properties.Add(new("channelCount", channelCount));
        }

        if (options.ChannelCountMode is { } channelCountMode)
        {
            properties.Add(new("channelCountMode", channelCountMode switch
            {
                WebAudio.ChannelCountMode.ClampedMax => "clamped-max",
                WebAudio.ChannelCountMode.Explicit => "explicit",
                _ => "max",
            }));
        }

        if (options.ChannelInterpretation is { } channelInterpretation)
        {
            properties.Add(new("channelInterpretation", channelInterpretation == WebAudio.ChannelInterpretation.Discrete ? "discrete" : "speakers"));
        }

        properties.Add(new("numberOfInputs", options.NumberOfInputs));
        properties.Add(new("numberOfOutputs", options.NumberOfOutputs));
        if (options.OutputChannelCount is { } outputChannelCount)
        {
            properties.Add(new("outputChannelCount", outputChannelCount));
        }

        if (options.ParameterData is { } parameterData)
        {
            properties.Add(new("parameterData", parameterData));
        }

        if (hasProcessorOptions)
        {
            properties.Add(new("processorOptions", processorOptions));
        }

        return properties;
    }

    /// <summary>Fires <c>processorerror</c> at the node (https://webaudio.github.io/web-audio-api/#dom-audioworkletnode-onprocessorerror) and reports the exception to the global scope.</summary>
    private void ReportProcessorError(AudioWorkletGlobalScope globalScope, Exception exception)
    {
        var error = globalScope.ScriptEngine?.ExtractErrorInformation(exception) ?? new ScriptError(exception.Message, exception);
        ScriptErrors.Report(exception, globalScope);
        Context.QueueMediaElementTask(() => FireEvent(new ErrorEvent("processorerror", new ErrorEventInit
        {
            Message = error.Message,
            Filename = error.Filename,
            Lineno = (uint)Math.Max(error.LineNumber, 0),
            Colno = (uint)Math.Max(error.ColumnNumber, 0),
        })));
    }

    /// <summary>The number of channels of an output for the render quantum.</summary>
    private int GetOutputChannelCount(int output)
    {
        if (_outputChannelCounts is { } counts)
            return counts[output];

        // A node with one input and one output: the output has the channels of the input
        return IsInputActive(0) ? Math.Max(GetInputBus(0).NumberOfChannels, 1) : 1;
    }

    private protected override void Process(long startFrame, int length)
    {
        var globalScope = Context.AudioWorklet.GlobalScope;
        if (_processCall is not { } call || !_callableProcess || globalScope is null || globalScope.IsTerminated)
        {
            SilenceOutputs();
            return;
        }

        var inputCount = (int)NumberOfInputs;
        var outputCount = (int)NumberOfOutputs;
        var inputChannels = new int[inputCount];
        for (var i = 0; i < inputCount; i++)
        {
            // https://webaudio.github.io/web-audio-api/#dom-audioworkletprocesscallback-inputs: an input without actively
            // processing connection has no channel
            inputChannels[i] = IsInputActive(i) ? GetInputBus(i).NumberOfChannels : 0;
        }

        var outputChannels = new int[outputCount];
        for (var i = 0; i < outputCount; i++)
        {
            outputChannels[i] = GetOutputChannelCount(i);
        }

        var parameterLengths = new int[_parameters.Length];
        for (var i = 0; i < _parameters.Length; i++)
        {
            parameterLengths[i] = _parameters[i].IsConstant ? 1 : length;
        }

        call.Prepare(inputChannels, outputChannels, _parameterNames, parameterLengths, length);
        for (var i = 0; i < inputCount; i++)
        {
            var bus = GetInputBus(i);
            for (var c = 0; c < inputChannels[i]; c++)
            {
                var destination = call.GetInput(i, c);
                if (bus.IsSilent)
                {
                    destination.Clear();
                }
                else
                {
                    bus.GetChannelSpan(c).CopyTo(destination);
                }
            }
        }

        for (var i = 0; i < _parameters.Length; i++)
        {
            var parameter = _parameters[i];
            var destination = call.GetParameter(i);
            if (parameter.IsConstant)
            {
                destination[0] = parameter.FinalValue;
            }
            else
            {
                parameter.Values.CopyTo(destination);
            }
        }

        bool active;
        globalScope.RenderingFrame = startFrame;
        try
        {
            active = call.Invoke();
        }
#pragma warning disable CA1031 // An exception of process() is reported to the node, and the node outputs silence
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _callableProcess = false;
            ReportProcessorError(globalScope, ex);
            SilenceOutputs();
            Context.OnGraphChanged();
            return;
        }
        finally
        {
            globalScope.RenderingFrame = null;
        }

        if (_activeSource != active)
        {
            _activeSource = active;
            Context.OnGraphChanged();
        }

        for (var i = 0; i < outputCount; i++)
        {
            var bus = GetOutputBus(i);
            bus.SetNumberOfChannels(outputChannels[i]);
            for (var c = 0; c < outputChannels[i]; c++)
            {
                call.GetOutput(i, c).CopyTo(bus.GetChannelSpan(c));
            }

            bus.MarkNonSilent();
        }
    }
}
