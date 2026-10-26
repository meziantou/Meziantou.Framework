namespace Meziantou.Framework.WebPlatform.WebAudio.Rendering;

/// <summary>
/// The audio of an input or an output of a node for one render quantum: a number of channels of
/// <see cref="Length"/> samples. A bus can be marked silent, so that the nodes skip the processing of silence.
/// </summary>
internal sealed class AudioBus
{
    private static readonly float SqrtHalf = MathF.Sqrt(0.5f);

    private float[][] _channels;

    public AudioBus(int numberOfChannels, int length)
    {
        Length = length;
        _channels = new float[Math.Max(numberOfChannels, 1)][];
        for (var i = 0; i < _channels.Length; i++)
        {
            _channels[i] = new float[length];
        }

        NumberOfChannels = numberOfChannels;
        IsSilent = true;
    }

    /// <summary>The number of samples of each channel (the render quantum size).</summary>
    public int Length { get; }

    /// <summary>The number of channels.</summary>
    public int NumberOfChannels { get; private set; }

    /// <summary>Whether every sample of every channel is zero (the samples are not necessarily zeroed when it is true).</summary>
    public bool IsSilent { get; private set; }

    /// <summary>Gets the samples of a channel.</summary>
    public float[] GetChannel(int index) => _channels[index];

    /// <summary>Gets the samples of a channel as a span.</summary>
    public Span<float> GetChannelSpan(int index) => _channels[index].AsSpan(0, Length);

    /// <summary>Changes the number of channels; the content of the channels is undefined.</summary>
    public void SetNumberOfChannels(int numberOfChannels)
    {
        if (numberOfChannels > _channels.Length)
        {
            var channels = new float[numberOfChannels][];
            Array.Copy(_channels, channels, _channels.Length);
            for (var i = _channels.Length; i < numberOfChannels; i++)
            {
                channels[i] = new float[Length];
            }

            _channels = channels;
        }

        NumberOfChannels = numberOfChannels;
    }

    /// <summary>Marks the bus as non-silent: the caller writes every sample of every channel.</summary>
    public void MarkNonSilent() => IsSilent = false;

    /// <summary>Sets the number of channels and the samples to zero.</summary>
    public void Zero(int numberOfChannels)
    {
        SetNumberOfChannels(numberOfChannels);
        if (!IsSilent)
        {
            for (var i = 0; i < _channels.Length; i++)
            {
                Array.Clear(_channels[i]);
            }

            IsSilent = true;
        }
    }

    /// <summary>Sets the samples to zero, keeping the number of channels.</summary>
    public void Zero() => Zero(NumberOfChannels);

    /// <summary>Copies another bus, which must have the same number of channels.</summary>
    public void CopyFrom(AudioBus source)
    {
        SetNumberOfChannels(source.NumberOfChannels);
        if (source.IsSilent)
        {
            Zero();
            return;
        }

        for (var i = 0; i < source.NumberOfChannels; i++)
        {
            source.GetChannelSpan(i).CopyTo(GetChannelSpan(i));
        }

        IsSilent = false;
    }

    /// <summary>
    /// Adds another bus to this one, up-mixing or down-mixing its channels to the channels of this bus
    /// (https://webaudio.github.io/web-audio-api/#channel-up-mixing-and-down-mixing).
    /// </summary>
    public void SumFrom(AudioBus source, ChannelInterpretation interpretation)
    {
        if (source.IsSilent)
            return;

        if (IsSilent)
        {
            for (var i = 0; i < NumberOfChannels; i++)
            {
                Array.Clear(_channels[i]);
            }

            IsSilent = false;
        }

        var inputChannels = source.NumberOfChannels;
        var outputChannels = NumberOfChannels;
        if (inputChannels == outputChannels)
        {
            for (var i = 0; i < outputChannels; i++)
            {
                Add(source.GetChannelSpan(i), GetChannelSpan(i));
            }

            return;
        }

        if (interpretation == ChannelInterpretation.Speakers && IsSpeakerLayout(inputChannels) && IsSpeakerLayout(outputChannels))
        {
            SumSpeakers(source, inputChannels, outputChannels);
            return;
        }

        // Discrete: the channels are copied in order, the missing ones are silent and the extra ones are dropped
        var count = Math.Min(inputChannels, outputChannels);
        for (var i = 0; i < count; i++)
        {
            Add(source.GetChannelSpan(i), GetChannelSpan(i));
        }
    }

    private static bool IsSpeakerLayout(int channels) => channels is 1 or 2 or 4 or 6;

    private void SumSpeakers(AudioBus source, int inputChannels, int outputChannels)
    {
        if (outputChannels > inputChannels)
        {
            // Up-mixing (https://webaudio.github.io/web-audio-api/#UpMix-sub)
            switch (inputChannels, outputChannels)
            {
                case (1, 2):
                case (1, 4):
                    Add(source.GetChannelSpan(0), GetChannelSpan(0));
                    Add(source.GetChannelSpan(0), GetChannelSpan(1));
                    break;

                case (1, 6):
                    Add(source.GetChannelSpan(0), GetChannelSpan(2));
                    break;

                case (2, 4):
                case (2, 6):
                    Add(source.GetChannelSpan(0), GetChannelSpan(0));
                    Add(source.GetChannelSpan(1), GetChannelSpan(1));
                    break;

                case (4, 6):
                    Add(source.GetChannelSpan(0), GetChannelSpan(0));
                    Add(source.GetChannelSpan(1), GetChannelSpan(1));
                    Add(source.GetChannelSpan(2), GetChannelSpan(4));
                    Add(source.GetChannelSpan(3), GetChannelSpan(5));
                    break;
            }

            return;
        }

        // Down-mixing (https://webaudio.github.io/web-audio-api/#down-mix)
        switch (inputChannels, outputChannels)
        {
            case (2, 1):
                AddScaled(source.GetChannelSpan(0), 0.5f, GetChannelSpan(0));
                AddScaled(source.GetChannelSpan(1), 0.5f, GetChannelSpan(0));
                break;

            case (4, 1):
                for (var i = 0; i < 4; i++)
                {
                    AddScaled(source.GetChannelSpan(i), 0.25f, GetChannelSpan(0));
                }

                break;

            case (6, 1):
                AddScaled(source.GetChannelSpan(0), SqrtHalf, GetChannelSpan(0));
                AddScaled(source.GetChannelSpan(1), SqrtHalf, GetChannelSpan(0));
                Add(source.GetChannelSpan(2), GetChannelSpan(0));
                AddScaled(source.GetChannelSpan(4), 0.5f, GetChannelSpan(0));
                AddScaled(source.GetChannelSpan(5), 0.5f, GetChannelSpan(0));
                break;

            case (4, 2):
                AddScaled(source.GetChannelSpan(0), 0.5f, GetChannelSpan(0));
                AddScaled(source.GetChannelSpan(2), 0.5f, GetChannelSpan(0));
                AddScaled(source.GetChannelSpan(1), 0.5f, GetChannelSpan(1));
                AddScaled(source.GetChannelSpan(3), 0.5f, GetChannelSpan(1));
                break;

            case (6, 2):
                Add(source.GetChannelSpan(0), GetChannelSpan(0));
                AddScaled(source.GetChannelSpan(2), SqrtHalf, GetChannelSpan(0));
                AddScaled(source.GetChannelSpan(4), SqrtHalf, GetChannelSpan(0));
                Add(source.GetChannelSpan(1), GetChannelSpan(1));
                AddScaled(source.GetChannelSpan(2), SqrtHalf, GetChannelSpan(1));
                AddScaled(source.GetChannelSpan(5), SqrtHalf, GetChannelSpan(1));
                break;

            case (6, 4):
                Add(source.GetChannelSpan(0), GetChannelSpan(0));
                AddScaled(source.GetChannelSpan(2), SqrtHalf, GetChannelSpan(0));
                Add(source.GetChannelSpan(1), GetChannelSpan(1));
                AddScaled(source.GetChannelSpan(2), SqrtHalf, GetChannelSpan(1));
                Add(source.GetChannelSpan(4), GetChannelSpan(2));
                Add(source.GetChannelSpan(5), GetChannelSpan(3));
                break;
        }
    }

    private static void Add(ReadOnlySpan<float> source, Span<float> destination)
    {
        for (var i = 0; i < destination.Length; i++)
        {
            destination[i] += source[i];
        }
    }

    private static void AddScaled(ReadOnlySpan<float> source, float scale, Span<float> destination)
    {
        for (var i = 0; i < destination.Length; i++)
        {
            destination[i] += source[i] * scale;
        }
    }
}
