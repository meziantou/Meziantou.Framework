using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.WebAudio.Rendering;

namespace Meziantou.Framework.WebPlatform.WebAudio;

/// <summary>The channel options of a node (https://webaudio.github.io/web-audio-api/#AudioNodeOptions).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#AudioNodeOptions"/>
[IdlInterface("AudioNodeOptions", "https://webaudio.github.io/web-audio-api/#AudioNodeOptions")]
public class AudioNodeOptions
{
    /// <summary>Gets the number of channels used to mix the inputs, or null for the default of the node.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audionodeoptions-channelcount"/>
    [IdlMember("channelCount", "https://webaudio.github.io/web-audio-api/#dom-audionodeoptions-channelcount")]
    public uint? ChannelCount { get; init; }

    /// <summary>Gets how the number of channels of the inputs is computed, or null for the default of the node.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audionodeoptions-channelcountmode"/>
    [IdlMember("channelCountMode", "https://webaudio.github.io/web-audio-api/#dom-audionodeoptions-channelcountmode")]
    public ChannelCountMode? ChannelCountMode { get; init; }

    /// <summary>Gets how the channels of the inputs are mixed, or null for the default of the node.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audionodeoptions-channelinterpretation"/>
    [IdlMember("channelInterpretation", "https://webaudio.github.io/web-audio-api/#dom-audionodeoptions-channelinterpretation")]
    public ChannelInterpretation? ChannelInterpretation { get; init; }
}

/// <summary>
/// A node of the audio routing graph of a <see cref="BaseAudioContext"/> (https://webaudio.github.io/web-audio-api/#AudioNode):
/// a source, a destination or a processing module, with inputs and outputs that are connected to other nodes and to the
/// <see cref="AudioParam"/> of other nodes.
/// </summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#AudioNode"/>
[IdlInterface("AudioNode", "https://webaudio.github.io/web-audio-api/#AudioNode")]
public abstract class AudioNode : EventTarget
{
    /// <summary>The maximum number of channels of a node (https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-createbuffer).</summary>
    internal const int MaxChannels = 32;

    private readonly NodeInput[] _inputs;
    private readonly NodeOutput[] _outputs;
    private uint _channelCount;
    private ChannelCountMode _channelCountMode;
    private ChannelInterpretation _channelInterpretation;

    // The frame until which the node is actively processing after its last active input (its tail time)
    private long _activeUntilFrame = long.MinValue;

    private protected AudioNode(BaseAudioContext context, int numberOfInputs, int numberOfOutputs, uint channelCount, ChannelCountMode channelCountMode, ChannelInterpretation channelInterpretation)
    {
        ArgumentNullException.ThrowIfNull(context);
        Context = context;
        _channelCount = channelCount;
        _channelCountMode = channelCountMode;
        _channelInterpretation = channelInterpretation;
        var length = context.RenderQuantumSizeInternal;
        _inputs = new NodeInput[numberOfInputs];
        for (var i = 0; i < numberOfInputs; i++)
        {
            _inputs[i] = new NodeInput(length);
        }

        _outputs = new NodeOutput[numberOfOutputs];
        for (var i = 0; i < numberOfOutputs; i++)
        {
            _outputs[i] = new NodeOutput(length);
        }

        context.RegisterNode(this);
    }

    /// <summary>Gets the context of the node.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audionode-context"/>
    [IdlMember("context", "https://webaudio.github.io/web-audio-api/#dom-audionode-context")]
    public BaseAudioContext Context { get; }

    /// <summary>Gets the number of inputs of the node.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audionode-numberofinputs"/>
    [IdlMember("numberOfInputs", "https://webaudio.github.io/web-audio-api/#dom-audionode-numberofinputs")]
    public uint NumberOfInputs => (uint)_inputs.Length;

    /// <summary>Gets the number of outputs of the node.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audionode-numberofoutputs"/>
    [IdlMember("numberOfOutputs", "https://webaudio.github.io/web-audio-api/#dom-audionode-numberofoutputs")]
    public uint NumberOfOutputs => (uint)_outputs.Length;

    /// <summary>Gets or sets the number of channels used to mix the inputs (see <see cref="ChannelCountMode"/>).</summary>
    /// <exception cref="DomException">The value is not supported by the node (a <c>NotSupportedError</c>, or an <c>IndexSizeError</c> for a destination).</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audionode-channelcount"/>
    [IdlMember("channelCount", "https://webaudio.github.io/web-audio-api/#dom-audionode-channelcount")]
    public uint ChannelCount
    {
        get => _channelCount;
        set
        {
            ValidateChannelCount(value);
            _channelCount = value;
            OnChannelPropertiesChanged();
        }
    }

    /// <summary>Gets or sets how the number of channels of the inputs is computed.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audionode-channelcountmode"/>
    [IdlMember("channelCountMode", "https://webaudio.github.io/web-audio-api/#dom-audionode-channelcountmode")]
    public ChannelCountMode ChannelCountMode
    {
        get => _channelCountMode;
        set
        {
            ValidateChannelCountMode(value);
            _channelCountMode = value;
            OnChannelPropertiesChanged();
        }
    }

    /// <summary>Gets or sets how the channels of the inputs are up-mixed and down-mixed.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audionode-channelinterpretation"/>
    [IdlMember("channelInterpretation", "https://webaudio.github.io/web-audio-api/#dom-audionode-channelinterpretation")]
    public ChannelInterpretation ChannelInterpretation
    {
        get => _channelInterpretation;
        set
        {
            ValidateChannelInterpretation(value);
            _channelInterpretation = value;
            OnChannelPropertiesChanged();
        }
    }

    internal override GlobalScope? RelevantGlobal => Context.Window ?? base.RelevantGlobal;

    /// <summary>Validates a new channel count. The default accepts 1 to 32 channels.</summary>
    private protected virtual void ValidateChannelCount(uint value)
    {
        if (value is 0 or > MaxChannels)
            throw new DomException($"The channel count {value} is not between 1 and {MaxChannels}", "NotSupportedError");
    }

    /// <summary>Validates a new channel count mode. The default accepts every mode.</summary>
    private protected virtual void ValidateChannelCountMode(ChannelCountMode value)
    {
    }

    /// <summary>Validates a new channel interpretation. The default accepts every interpretation.</summary>
    private protected virtual void ValidateChannelInterpretation(ChannelInterpretation value)
    {
    }

    private protected virtual void OnChannelPropertiesChanged()
    {
    }

    /// <summary>Applies the channel options given to the constructor of a node (https://webaudio.github.io/web-audio-api/#audionode-constructor-init).</summary>
    private protected void ApplyOptions(AudioNodeOptions? options)
    {
        if (options is null)
            return;

        if (options.ChannelCount is { } channelCount)
        {
            ChannelCount = channelCount;
        }

        if (options.ChannelCountMode is { } channelCountMode)
        {
            ChannelCountMode = channelCountMode;
        }

        if (options.ChannelInterpretation is { } channelInterpretation)
        {
            ChannelInterpretation = channelInterpretation;
        }
    }

    /// <summary>Connects an output of the node to an input of another node.</summary>
    /// <returns>The destination node, so that the calls can be chained.</returns>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audionode-connect"/>
    [IdlMember("connect", "https://webaudio.github.io/web-audio-api/#dom-audionode-connect")]
    public AudioNode Connect(AudioNode destinationNode, uint output = 0, uint input = 0)
    {
        ArgumentNullException.ThrowIfNull(destinationNode);
        if (destinationNode.Context != Context)
            throw new DomException("The destination node belongs to another context", "InvalidAccessError");

        if (output >= NumberOfOutputs)
            throw new DomException($"The output {output} does not exist", "IndexSizeError");

        if (input >= destinationNode.NumberOfInputs)
            throw new DomException($"The input {input} does not exist", "IndexSizeError");

        var connections = _outputs[output].Nodes;
        if (!connections.Contains((destinationNode, (int)input)))
        {
            connections.Add((destinationNode, (int)input));
            destinationNode._inputs[input].Connections.Add((this, (int)output));
            Context.OnGraphChanged();
            destinationNode.OnInputConnected((int)input);
        }

        return destinationNode;
    }

    /// <summary>Connects an output of the node to a parameter of another node.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audionode-connect-destinationparam-output"/>
    [IdlMember("connect", "https://webaudio.github.io/web-audio-api/#dom-audionode-connect-destinationparam-output")]
    public void Connect(AudioParam destinationParam, uint output = 0)
    {
        ArgumentNullException.ThrowIfNull(destinationParam);
        if (destinationParam.Context != Context)
            throw new DomException("The destination parameter belongs to another context", "InvalidAccessError");

        if (output >= NumberOfOutputs)
            throw new DomException($"The output {output} does not exist", "IndexSizeError");

        var connections = _outputs[output].Params;
        if (!connections.Contains(destinationParam))
        {
            connections.Add(destinationParam);
            destinationParam.Inputs.Add((this, (int)output));
            Context.OnGraphChanged();
        }
    }

    /// <summary>Disconnects all the outgoing connections.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audionode-disconnect"/>
    [IdlMember("disconnect", "https://webaudio.github.io/web-audio-api/#dom-audionode-disconnect")]
    public void Disconnect()
    {
        for (var i = 0; i < _outputs.Length; i++)
        {
            DisconnectOutput(i, node: null, input: -1, param: null, includeParams: true);
        }
    }

    /// <summary>Disconnects all the outgoing connections of an output.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audionode-disconnect-output"/>
    [IdlMember("disconnect", "https://webaudio.github.io/web-audio-api/#dom-audionode-disconnect-output")]
    public void Disconnect(uint output)
    {
        ValidateOutput(output);
        DisconnectOutput((int)output, node: null, input: -1, param: null, includeParams: true);
    }

    /// <summary>Disconnects all the connections to a node.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audionode-disconnect-destinationnode"/>
    [IdlMember("disconnect", "https://webaudio.github.io/web-audio-api/#dom-audionode-disconnect-destinationnode")]
    public void Disconnect(AudioNode destinationNode)
    {
        ArgumentNullException.ThrowIfNull(destinationNode);
        var disconnected = false;
        for (var i = 0; i < _outputs.Length; i++)
        {
            disconnected |= DisconnectOutput(i, destinationNode, input: -1, param: null, includeParams: false);
        }

        if (!disconnected)
            throw new DomException("The node is not connected to the destination node", "InvalidAccessError");
    }

    /// <summary>Disconnects the connections of an output to a node.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audionode-disconnect-destinationnode-output"/>
    [IdlMember("disconnect", "https://webaudio.github.io/web-audio-api/#dom-audionode-disconnect-destinationnode-output")]
    public void Disconnect(AudioNode destinationNode, uint output)
    {
        ArgumentNullException.ThrowIfNull(destinationNode);
        ValidateOutput(output);
        if (!DisconnectOutput((int)output, destinationNode, input: -1, param: null, includeParams: false))
            throw new DomException($"The output {output} is not connected to the destination node", "InvalidAccessError");
    }

    /// <summary>Disconnects the connection of an output to an input of a node.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audionode-disconnect-destinationnode-output-input"/>
    [IdlMember("disconnect", "https://webaudio.github.io/web-audio-api/#dom-audionode-disconnect-destinationnode-output-input")]
    public void Disconnect(AudioNode destinationNode, uint output, uint input)
    {
        ArgumentNullException.ThrowIfNull(destinationNode);
        ValidateOutput(output);
        if (input >= destinationNode.NumberOfInputs)
            throw new DomException($"The input {input} does not exist", "IndexSizeError");

        if (!DisconnectOutput((int)output, destinationNode, (int)input, param: null, includeParams: false))
            throw new DomException($"The output {output} is not connected to the input {input} of the destination node", "InvalidAccessError");
    }

    /// <summary>Disconnects all the connections to a parameter.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audionode-disconnect-destinationparam"/>
    [IdlMember("disconnect", "https://webaudio.github.io/web-audio-api/#dom-audionode-disconnect-destinationparam")]
    public void Disconnect(AudioParam destinationParam)
    {
        ArgumentNullException.ThrowIfNull(destinationParam);
        var disconnected = false;
        for (var i = 0; i < _outputs.Length; i++)
        {
            disconnected |= DisconnectOutput(i, node: null, input: -1, destinationParam, includeParams: true);
        }

        if (!disconnected)
            throw new DomException("The node is not connected to the destination parameter", "InvalidAccessError");
    }

    /// <summary>Disconnects the connection of an output to a parameter.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audionode-disconnect-destinationparam-output"/>
    [IdlMember("disconnect", "https://webaudio.github.io/web-audio-api/#dom-audionode-disconnect-destinationparam-output")]
    public void Disconnect(AudioParam destinationParam, uint output)
    {
        ArgumentNullException.ThrowIfNull(destinationParam);
        ValidateOutput(output);
        if (!DisconnectOutput((int)output, node: null, input: -1, destinationParam, includeParams: true))
            throw new DomException($"The output {output} is not connected to the destination parameter", "InvalidAccessError");
    }

    private void ValidateOutput(uint output)
    {
        if (output >= NumberOfOutputs)
            throw new DomException($"The output {output} does not exist", "IndexSizeError");
    }

    /// <summary>
    /// Removes the connections of an output: to a node (all its inputs when <paramref name="input"/> is -1, all the nodes when
    /// <paramref name="node"/> is null) and to the parameters (a parameter, or all of them when <paramref name="param"/> is null).
    /// </summary>
    private bool DisconnectOutput(int output, AudioNode? node, int input, AudioParam? param, bool includeParams)
    {
        var disconnected = false;
        var nodes = _outputs[output].Nodes;
        if (param is null)
        {
            for (var i = nodes.Count - 1; i >= 0; i--)
            {
                var (destination, destinationInput) = nodes[i];
                if ((node is null || destination == node) && (input < 0 || destinationInput == input))
                {
                    nodes.RemoveAt(i);
                    destination._inputs[destinationInput].Connections.Remove((this, output));
                    destination.OnInputDisconnected(destinationInput);
                    disconnected = true;
                }
            }
        }

        if (includeParams)
        {
            var parameters = _outputs[output].Params;
            for (var i = parameters.Count - 1; i >= 0; i--)
            {
                var destination = parameters[i];
                if (param is null || destination == param)
                {
                    parameters.RemoveAt(i);
                    destination.Inputs.Remove((this, output));
                    disconnected = true;
                }
            }
        }

        if (disconnected)
        {
            Context.OnGraphChanged();
        }

        return disconnected;
    }

    /// <summary>Called when a connection is added to an input.</summary>
    private protected virtual void OnInputConnected(int input)
    {
    }

    /// <summary>Called when a connection is removed from an input.</summary>
    private protected virtual void OnInputDisconnected(int input)
    {
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Rendering
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>The parameters of the node, computed before each render quantum it processes.</summary>
    private protected virtual IEnumerable<AudioParam> NodeParameters => [];

    /// <summary>Whether the node is processed even when it has no connection (e.g. a started source, to fire its ended event).</summary>
    internal virtual bool RequiresProcessing => false;

    /// <summary>Whether the node has a connection (an input, an output or a parameter connection).</summary>
    internal bool HasConnections
    {
        get
        {
            foreach (var input in _inputs)
            {
                if (input.Connections.Count > 0)
                    return true;
            }

            foreach (var output in _outputs)
            {
                if (output.Nodes.Count > 0 || output.Params.Count > 0)
                    return true;
            }

            foreach (var parameter in NodeParameters)
            {
                if (parameter.Inputs.Count > 0)
                    return true;
            }

            return false;
        }
    }

    /// <summary>The nodes whose outputs this node reads (its inputs and the inputs of its parameters).</summary>
    internal IEnumerable<AudioNode> GetUpstreamNodes()
    {
        foreach (var input in _inputs)
        {
            foreach (var (node, _) in input.Connections)
            {
                yield return node;
            }
        }

        foreach (var parameter in NodeParameters)
        {
            foreach (var (node, _) in parameter.Inputs)
            {
                yield return node;
            }
        }
    }

    /// <summary>The nodes that read the outputs of this node.</summary>
    internal IEnumerable<AudioNode> GetDownstreamNodes()
    {
        foreach (var output in _outputs)
        {
            foreach (var (node, _) in output.Nodes)
            {
                yield return node;
            }

            foreach (var parameter in output.Params)
            {
                if (parameter.Owner is { } owner)
                    yield return owner;
            }
        }
    }

    /// <summary>The nodes connected to the parameters of this node.</summary>
    internal IEnumerable<AudioNode> GetParameterInputNodes()
    {
        foreach (var parameter in NodeParameters)
        {
            foreach (var (node, _) in parameter.Inputs)
            {
                yield return node;
            }
        }
    }

    /// <summary>Whether the node can break a cycle of the graph (a <see cref="DelayNode"/>).</summary>
    internal virtual bool CanBreakCycles => false;

    /// <summary>
    /// Whether the node breaks a cycle: its output is produced before the other nodes of the render quantum
    /// (the output is produced by <see cref="RenderQuantum"/>), and its input is read after them (<see cref="RenderCycleInput"/>).
    /// </summary>
    internal bool BreaksCycle { get; set; }

    /// <summary>Whether the node is in a cycle that has no <see cref="DelayNode"/>: it outputs silence (https://webaudio.github.io/web-audio-api/#rendering-loop).</summary>
    internal bool IsMuted { get; set; }

    /// <summary>Whether the node was actively processing during the last render quantum (https://webaudio.github.io/web-audio-api/#actively-processing).</summary>
    internal bool IsActivelyProcessing { get; private set; }

    /// <summary>The tail time of the node in frames: how long its output can be non-silent after its inputs become silent.</summary>
    private protected virtual long TailFrames => 0;

    /// <summary>Whether a parameter of the node must be computed at k-rate whatever its automation rate (e.g. the parameters of an HRTF panner).</summary>
    internal virtual bool ForcesKRate(AudioParam parameter) => false;

    /// <summary>Gets the bus of an output for the current render quantum.</summary>
    internal AudioBus GetOutputBus(int output) => _outputs[output].Bus;

    /// <summary>Gets the bus of an input for the current render quantum (mixed by <see cref="RenderQuantum"/>).</summary>
    private protected AudioBus GetInputBus(int input) => _inputs[input].Bus;

    /// <summary>Whether an input has connections from actively processing nodes in the current render quantum.</summary>
    private protected bool IsInputActive(int input) => _inputs[input].IsActive;

    /// <summary>The number of connections of an input.</summary>
    private protected int GetInputConnectionCount(int input) => _inputs[input].Connections.Count;

    /// <summary>
    /// Processes a render quantum: mixes the inputs, computes the parameters and the outputs. A node that is not actively
    /// processing outputs a single channel of silence.
    /// </summary>
    internal void RenderQuantum(long startFrame, int length)
    {
        if (IsMuted)
        {
            IsActivelyProcessing = false;
            SilenceOutputs();
            return;
        }

        if (BreaksCycle)
        {
            foreach (var parameter in NodeParameters)
            {
                parameter.Compute(startFrame);
            }

            IsActivelyProcessing = ProcessCycleOutput(startFrame, length);
            return;
        }

        var anyActiveInput = false;
        for (var i = 0; i < _inputs.Length; i++)
        {
            anyActiveInput |= MixInput(_inputs[i]);
        }

        foreach (var parameter in NodeParameters)
        {
            parameter.Compute(startFrame);
        }

        if (anyActiveInput)
        {
            var tail = TailFrames;
            _activeUntilFrame = tail >= long.MaxValue - startFrame - length ? long.MaxValue : startFrame + length + tail;
        }

        var active = IsSourceActive(startFrame, length) || startFrame < _activeUntilFrame;
        if (!active)
        {
            if (IsActivelyProcessing)
            {
                OnBecameInactive();
            }

            IsActivelyProcessing = false;
            SilenceOutputs();
            return;
        }

        IsActivelyProcessing = true;
        Process(startFrame, length);
    }

    /// <summary>Reads the inputs of a node that breaks a cycle, once the other nodes processed the render quantum.</summary>
    internal void RenderCycleInput(long startFrame, int length)
    {
        var anyActiveInput = false;
        for (var i = 0; i < _inputs.Length; i++)
        {
            anyActiveInput |= MixInput(_inputs[i]);
        }

        ProcessCycleInput(startFrame, length, anyActiveInput);
    }

    /// <summary>Produces the output of a node that breaks a cycle (before its input is known). Returns whether the node is actively processing.</summary>
    private protected virtual bool ProcessCycleOutput(long startFrame, int length) => throw new InvalidOperationException("The node cannot break a cycle");

    /// <summary>Consumes the input of a node that breaks a cycle, after the other nodes processed the render quantum.</summary>
    private protected virtual void ProcessCycleInput(long startFrame, int length, bool anyActiveInput) => throw new InvalidOperationException("The node cannot break a cycle");

    /// <summary>Called when the node stops actively processing (e.g. to reset the state of a filter).</summary>
    private protected virtual void OnBecameInactive()
    {
    }

    /// <summary>Whether a source node plays during the render quantum, or a node processes without active input. The default returns false.</summary>
    private protected virtual bool IsSourceActive(long startFrame, int length) => false;

    /// <summary>Computes the outputs of a render quantum from the inputs (mixed) and the parameters (computed).</summary>
    private protected abstract void Process(long startFrame, int length);

    /// <summary>Outputs a single channel of silence on every output.</summary>
    private protected void SilenceOutputs()
    {
        foreach (var output in _outputs)
        {
            output.Bus.Zero(1);
        }
    }

    /// <summary>
    /// Mixes the connections of an input (https://webaudio.github.io/web-audio-api/#channel-up-mixing-and-down-mixing). Returns
    /// whether a connection comes from an actively processing node.
    /// </summary>
    private bool MixInput(NodeInput input)
    {
        var connections = input.Connections;
        var active = false;
        var maxChannels = 0;
        foreach (var (node, output) in connections)
        {
            if (node.IsActivelyProcessing)
            {
                active = true;
            }

            var channels = node._outputs[output].Bus.NumberOfChannels;
            if (channels > maxChannels)
            {
                maxChannels = channels;
            }
        }

        input.IsActive = active;
        var computedNumberOfChannels = ComputeNumberOfChannels(maxChannels == 0 ? 1 : maxChannels);
        var bus = input.Bus;
        bus.Zero(computedNumberOfChannels);
        foreach (var (node, output) in connections)
        {
            bus.SumFrom(node._outputs[output].Bus, _channelInterpretation);
        }

        return active;
    }

    /// <summary>The computedNumberOfChannels of an input from the maximum number of channels of its connections.</summary>
    private protected int ComputeNumberOfChannels(int maxChannels) => _channelCountMode switch
    {
        ChannelCountMode.Max => maxChannels,
        ChannelCountMode.ClampedMax => Math.Min(maxChannels, (int)_channelCount),
        _ => (int)_channelCount,
    };

    private sealed class NodeInput(int length)
    {
        public List<(AudioNode Node, int Output)> Connections { get; } = [];

        public AudioBus Bus { get; } = new(1, length);

        public bool IsActive { get; set; }
    }

    private sealed class NodeOutput(int length)
    {
        public List<(AudioNode Node, int Input)> Nodes { get; } = [];

        public List<AudioParam> Params { get; } = [];

        public AudioBus Bus { get; } = new(1, length);
    }
}
