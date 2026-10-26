namespace Meziantou.Framework.WebPlatform.WebAudio.Rendering;

/// <summary>
/// The call of the <c>process()</c> method of an <see cref="AudioWorkletProcessor"/>
/// (https://webaudio.github.io/web-audio-api/#dom-audioworkletprocessor-process): the arrays of the inputs, of the outputs and
/// of the parameters are objects of the script engine whose samples are shared with the .NET code. They are reused while
/// their shape does not change, as in the browsers.
/// </summary>
internal abstract class AudioWorkletProcessCall
{
    /// <summary>
    /// Prepares the arrays of a render quantum: new arrays are created when the number of channels of an input or an output,
    /// or the length of the values of a parameter, changes, or when an array was transferred by the script. The output
    /// arrays are filled with zeros.
    /// </summary>
    public abstract void Prepare(int[] inputChannels, int[] outputChannels, string[] parameterNames, int[] parameterLengths, int frames);

    /// <summary>Gets the samples of a channel of an input.</summary>
    public abstract Span<float> GetInput(int input, int channel);

    /// <summary>Gets the samples of a channel of an output (empty when the script detached its buffer).</summary>
    public abstract Span<float> GetOutput(int output, int channel);

    /// <summary>Gets the values of a parameter.</summary>
    public abstract Span<float> GetParameter(int index);

    /// <summary>
    /// Calls <c>process(inputs, outputs, parameters)</c> and returns its result converted to a boolean. The exceptions of the
    /// script are rethrown (a <c>TypeError</c> when <c>process</c> is not callable).
    /// </summary>
    public abstract bool Invoke();
}
