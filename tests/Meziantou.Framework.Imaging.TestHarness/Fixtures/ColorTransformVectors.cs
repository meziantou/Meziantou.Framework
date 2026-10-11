using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>Reads the vectors of a <see cref="ColorTransformEntry"/>.</summary>
public static class ColorTransformVectors
{
    /// <summary>Reads the source and the reference destination samples of a color transform.</summary>
    /// <param name="rootDirectory">The corpus root directory.</param>
    /// <param name="entry">The entry.</param>
    /// <returns>The interleaved source samples and the interleaved reference samples.</returns>
    /// <exception cref="InvalidDataException">The file does not exist or does not have the declared size.</exception>
    public static (ushort[] Source, ushort[] Expected) Read(FullPath rootDirectory, ColorTransformEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (FixturePaths.TryResolve(rootDirectory, entry.Vectors.Path, out var error) is not { } path)
            throw new InvalidDataException(error);

        var data = File.ReadAllBytes(path);
        var perColor = entry.SourceChannels + entry.DestinationChannels;
        if (data.Length != (long)entry.SampleCount * perColor * 2)
            throw new InvalidDataException($"'{entry.Vectors.Path}' has {data.Length} bytes but {entry.SampleCount} colors of {perColor} 16-bit samples are declared.");

        var source = new ushort[entry.SampleCount * entry.SourceChannels];
        var expected = new ushort[entry.SampleCount * entry.DestinationChannels];
        var position = 0;
        for (var i = 0; i < entry.SampleCount; i++)
        {
            for (var channel = 0; channel < entry.SourceChannels; channel++, position += 2)
            {
                source[(i * entry.SourceChannels) + channel] = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(position));
            }

            for (var channel = 0; channel < entry.DestinationChannels; channel++, position += 2)
            {
                expected[(i * entry.DestinationChannels) + channel] = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(position));
            }
        }

        return (source, expected);
    }
}
