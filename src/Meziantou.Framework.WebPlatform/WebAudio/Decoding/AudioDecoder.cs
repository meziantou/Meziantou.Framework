namespace Meziantou.Framework.WebPlatform.WebAudio.Decoding;

/// <summary>The linear PCM of a decoded audio file: one array of samples per channel.</summary>
internal sealed record DecodedAudio(float[][] Channels, float SampleRate);

/// <summary>
/// Decodes the audio files of <c>decodeAudioData()</c> (https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-decodeaudiodata):
/// the format is sniffed from the content (WAV or FLAC), the samples are decoded to linear PCM, then resampled to the sample
/// rate of the context.
/// </summary>
internal static class AudioDecoder
{
    /// <summary>Decodes an audio file and resamples it to <paramref name="sampleRate"/>.</summary>
    /// <exception cref="InvalidDataException">The format is not supported, the file is invalid, or the decoded audio does not fit in an <see cref="AudioBuffer"/>.</exception>
    public static DecodedAudio Decode(ReadOnlySpan<byte> data, float sampleRate)
    {
        var decoded = DecodeFile(data, sampleRate);
        if (decoded.Channels.Length is 0 or > AudioNode.MaxChannels)
            throw new InvalidDataException($"The audio has {decoded.Channels.Length} channels: an AudioBuffer has from 1 to {AudioNode.MaxChannels} channels");

        var length = decoded.Channels[0].Length;
        if (length == 0)
            throw new InvalidDataException("The audio has no sample frame");

        if (decoded.SampleRate == sampleRate)
            return decoded;

        var resampledLength = GetResampledLength(length, decoded.SampleRate, sampleRate);
        var channels = new float[decoded.Channels.Length][];
        for (var i = 0; i < channels.Length; i++)
        {
            channels[i] = Resampler.Resample(decoded.Channels[i], decoded.SampleRate, sampleRate, (int)resampledLength);
        }

        return new DecodedAudio(channels, sampleRate);
    }

    private static DecodedAudio DecodeFile(ReadOnlySpan<byte> data, float sampleRate)
    {
        if (WavDecoder.IsWav(data))
            return WavDecoder.Decode(data, (rate, frames) => CheckLength(frames, rate, sampleRate));

        if (FlacDecoder.TryGetStreamOffset(data, out var offset))
            return FlacDecoder.Decode(data[offset..], (rate, frames) => CheckLength(frames, rate, sampleRate));

        throw new InvalidDataException("The format of the audio is not supported (only WAV and FLAC are decoded)");
    }

    /// <summary>The number of frames of the audio resampled to the sample rate of the context.</summary>
    internal static long GetResampledLength(long frames, double sourceRate, double targetRate)
    {
        if (sourceRate == targetRate)
            return frames;

        // The exact number of frames when the ratio is rational in the sample rates (e.g. 44100 Hz to 48000 Hz)
        return (long)Math.Ceiling((frames * targetRate / sourceRate) - 1e-7);
    }

    /// <summary>Rejects the audio whose resampled length does not fit in an <see cref="AudioBuffer"/>, before its samples are decoded.</summary>
    private static void CheckLength(long frames, double sourceRate, double targetRate)
    {
        if (sourceRate is < AudioBuffer.MinSampleRate or > AudioBuffer.MaxSampleRate)
            throw new InvalidDataException($"The sample rate {sourceRate} of the audio is not between {AudioBuffer.MinSampleRate} and {AudioBuffer.MaxSampleRate}");

        var length = GetResampledLength(frames, sourceRate, targetRate);
        if (length * sizeof(float) > Array.MaxLength)
            throw new InvalidDataException($"The audio of {frames} frames resampled from {sourceRate} Hz to {targetRate} Hz has {length} frames: it does not fit in an AudioBuffer");
    }
}
