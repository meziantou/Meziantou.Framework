using Meziantou.Framework.WebPlatform.WebAudio;
using Meziantou.Framework.WebPlatform.WebAudio.Rendering;

namespace Meziantou.Framework.WebPlatform.Scripting;

// The bindings of the audio worklets (https://webaudio.github.io/web-audio-api/#AudioWorklet): the conversions of the
// processor classes, and the calls of their process() method
internal sealed partial class JsWorkletClassConstructor<TAdapter, TValue>
{
    internal override IReadOnlyList<AudioParamDescriptor>? GetAudioParamDescriptors()
    {
        var value = realm.Adapter.Get(Function, realm.Key("parameterDescriptors"));
        if (realm.Adapter.IsUndefined(value))
            return null;

        return realm.ToSequence(value, static (r, v) => ToAudioParamDescriptor(r, v));
    }

    /// <summary>Converts a value to an <see cref="AudioParamDescriptor"/> (https://webidl.spec.whatwg.org/#js-dictionary): the members in lexicographical order.</summary>
    private static AudioParamDescriptor ToAudioParamDescriptor(WebIdlRealm<TAdapter, TValue> r, TValue value)
    {
        r.CheckDictionary(value, "AudioParamDescriptor");
        var automationRate = r.GetDictionaryMember(value, "automationRate");
        var defaultValue = r.GetDictionaryMember(value, "defaultValue");
        var maxValue = r.GetDictionaryMember(value, "maxValue");
        var minValue = r.GetDictionaryMember(value, "minValue");
        var name = r.GetDictionaryMember(value, "name");
        var rate = AutomationRate.ARate;
        if (!r.Adapter.IsUndefined(automationRate))
        {
            rate = r.ToDomString(automationRate) switch
            {
                "a-rate" => AutomationRate.ARate,
                "k-rate" => AutomationRate.KRate,
                var other => throw r.CreateTypeError($"'{other}' is not a valid value of the AutomationRate enumeration"),
            };
        }

        if (r.Adapter.IsUndefined(name))
            throw r.CreateTypeError("The member 'name' of AudioParamDescriptor is required");

        return new AudioParamDescriptor
        {
            AutomationRate = rate,
            DefaultValue = r.Adapter.IsUndefined(defaultValue) ? 0 : r.ToFloat(defaultValue, restricted: true),
            MaxValue = r.Adapter.IsUndefined(maxValue) ? float.MaxValue : r.ToFloat(maxValue, restricted: true),
            MinValue = r.Adapter.IsUndefined(minValue) ? -float.MaxValue : r.ToFloat(minValue, restricted: true),
            Name = r.ToDomString(name),
        };
    }

    internal override object CreateObject(IReadOnlyList<KeyValuePair<string, object?>> properties)
    {
        return realm.Run(() =>
        {
            var adapter = realm.Adapter;
            var result = adapter.CreateObject(realm.ObjectPrototype);
            foreach (var (name, value) in properties)
            {
                adapter.DefineOwnProperty(result, realm.Key(name), JsPropertyDescriptor<TValue>.Data(ToPropertyValue(value), writable: true, enumerable: true, configurable: true));
            }

            return result;
        });
    }

    private TValue ToPropertyValue(object? value)
    {
        var adapter = realm.Adapter;
        switch (value)
        {
            case IReadOnlyList<uint> numbers:
            {
                var items = new TValue[numbers.Count];
                for (var i = 0; i < items.Length; i++)
                {
                    items[i] = adapter.FromNumber(numbers[i]);
                }

                return adapter.CreateArray(items);
            }

            case IReadOnlyList<KeyValuePair<string, double>> record:
            {
                var result = adapter.CreateObject(realm.ObjectPrototype);
                foreach (var (key, number) in record)
                {
                    adapter.DefineOwnProperty(result, realm.Key(key), JsPropertyDescriptor<TValue>.Data(adapter.FromNumber(number), writable: true, enumerable: true, configurable: true));
                }

                return result;
            }

            default:
                return realm.TryAsValue(value, out var jsValue) ? jsValue : realm.ToJs(value);
        }
    }

    internal override void EnterRenderQuantum() => realm.Agent.Host.OnCallbackInvoking();

    internal override void ExitRenderQuantum(Dom.GlobalScope global) => realm.Agent.Host.OnCallbackInvoked(global);

    internal override AudioWorkletProcessCall CreateProcessCall(object processor) => new JsAudioWorkletProcessCall<TAdapter, TValue>(realm, (TValue)processor);
}

/// <summary>The call of the <c>process()</c> method of a processor of the scripts, with arrays reused while their shape does not change.</summary>
internal sealed class JsAudioWorkletProcessCall<TAdapter, TValue>(WebIdlRealm<TAdapter, TValue> realm, TValue processor) : AudioWorkletProcessCall
    where TAdapter : struct, IJsAdapter<TValue>
    where TValue : class
{
    private int[] _inputChannels = [];
    private int[] _outputChannels = [];
    private string[] _parameterNames = [];
    private int[] _parameterLengths = [];
    private int _frames;
    private TValue? _inputs;
    private TValue? _outputs;
    private TValue? _parameters;
    private TValue?[][] _inputArrays = [];
    private TValue?[][] _outputArrays = [];
    private TValue?[] _parameterArrays = [];
    private TValue? _freeze;

    public override void Prepare(int[] inputChannels, int[] outputChannels, string[] parameterNames, int[] parameterLengths, int frames)
    {
        var sizeChanged = frames != _frames;
        _frames = frames;
        if (sizeChanged || !inputChannels.AsSpan().SequenceEqual(_inputChannels) || IsDetached(_inputArrays))
        {
            _inputChannels = [.. inputChannels];
            (_inputs, _inputArrays) = CreateChannelArrays(inputChannels);
        }

        if (sizeChanged || !outputChannels.AsSpan().SequenceEqual(_outputChannels) || IsDetached(_outputArrays))
        {
            _outputChannels = [.. outputChannels];
            (_outputs, _outputArrays) = CreateChannelArrays(outputChannels);
        }
        else
        {
            // The outputs are filled with zeros
            foreach (var arrays in _outputArrays)
            {
                foreach (var array in arrays)
                {
                    GetSamples(array).Clear();
                }
            }
        }

        if (sizeChanged || !parameterLengths.AsSpan().SequenceEqual(_parameterLengths) || !parameterNames.AsSpan().SequenceEqual(_parameterNames) || IsDetached([_parameterArrays]))
        {
            _parameterNames = [.. parameterNames];
            _parameterLengths = [.. parameterLengths];
            (_parameters, _parameterArrays) = CreateParameterArrays(parameterNames, parameterLengths);
        }
    }

    public override Span<float> GetInput(int input, int channel) => GetSamples(_inputArrays[input][channel]);

    public override Span<float> GetOutput(int output, int channel) => GetSamples(_outputArrays[output][channel]);

    public override Span<float> GetParameter(int index) => GetSamples(_parameterArrays[index]);

    public override bool Invoke()
    {
        realm.Agent.Host.OnCallbackInvoking();
        try
        {
            return realm.Run(() =>
            {
                var adapter = realm.Adapter;

                // https://webaudio.github.io/web-audio-api/#rendering-a-graph: the process method is looked up at each call
                var process = adapter.Get(processor, realm.Key("process"));
                if (!adapter.IsCallable(process))
                    throw realm.CreateTypeError("The 'process' property of the processor is not a function");

                var result = adapter.Call(process, processor, [_inputs!, _outputs!, _parameters!]);
                return adapter.ToBoolean(result);
            });
        }
        finally
        {
            realm.Agent.Host.OnCallbackInvoked(realm.GlobalScope);
        }
    }

    /// <summary>The samples of a Float32Array (empty when its buffer is detached).</summary>
    private Span<float> GetSamples(TValue? array)
    {
        if (array is null || !realm.Adapter.TryGetBufferSource(array, out _, out _, out var bytes))
            return default;

        return unsafe(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(bytes.Span));
    }

    private bool IsDetached(TValue?[][] groups)
    {
        foreach (var arrays in groups)
        {
            foreach (var array in arrays)
            {
                if (array is not null && GetSamples(array).IsEmpty)
                    return true;
            }
        }

        return false;
    }

    private TValue Freeze(TValue value)
    {
        _freeze ??= realm.EvaluateBuiltIn("Object.freeze");
        return realm.Adapter.Call(_freeze, realm.Adapter.Undefined, [value]);
    }

    /// <summary>Creates the frozen array of the frozen arrays of the Float32Arrays of the channels.</summary>
    private (TValue Array, TValue?[][] Channels) CreateChannelArrays(int[] channels)
    {
        return realm.Run(() =>
        {
            var groups = new TValue?[channels.Length][];
            var items = new TValue[channels.Length];
            for (var i = 0; i < channels.Length; i++)
            {
                var arrays = new TValue?[channels[i]];
                var channelItems = new TValue[channels[i]];
                for (var c = 0; c < channels[i]; c++)
                {
                    arrays[c] = channelItems[c] = realm.FromBuffer(Scripting.JsBufferKind.Float32Array, new byte[_frames * sizeof(float)]);
                }

                groups[i] = arrays;
                items[i] = Freeze(realm.Adapter.CreateArray(channelItems));
            }

            return (Freeze(realm.Adapter.CreateArray(items)), groups);
        });
    }

    /// <summary>Creates the frozen object of the values of the parameters, by name.</summary>
    private (TValue Object, TValue?[] Arrays) CreateParameterArrays(string[] names, int[] lengths)
    {
        return realm.Run(() =>
        {
            var arrays = new TValue?[names.Length];
            var result = realm.Adapter.CreateObject(realm.ObjectPrototype);
            for (var i = 0; i < names.Length; i++)
            {
                arrays[i] = realm.FromBuffer(Scripting.JsBufferKind.Float32Array, new byte[lengths[i] * sizeof(float)]);
                // https://webaudio.github.io/web-audio-api/#dom-audioworkletprocessor-process: CreateDataProperty, which ignores
                // the setters and the read-only properties of Object.prototype
                realm.Adapter.DefineOwnProperty(result, realm.Key(names[i]), JsPropertyDescriptor<TValue>.Data(arrays[i]!, writable: true, enumerable: true, configurable: true));
            }

            return (Freeze(result), arrays);
        });
    }
}
