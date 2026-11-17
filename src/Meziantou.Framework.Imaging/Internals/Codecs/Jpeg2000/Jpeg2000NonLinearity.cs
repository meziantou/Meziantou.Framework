using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// An NLT marker segment (ISO/IEC 15444-2, A.3.12): the non-linearity point transformation applied to the samples of a
/// component after the inverse component and wavelet transformations.
/// </summary>
/// <param name="Precision">The number of bits of the samples the transformation applies to (BDnlt).</param>
/// <param name="IsSigned">Whether those samples are signed.</param>
/// <param name="Type">The type of the transformation (Tnlt): 0 none, 1 gamma, 2 lookup table, 3 binary complement.</param>
internal readonly record struct Jpeg2000NonLinearity(int Precision, bool IsSigned, int Type)
{
    /// <summary>The binary complement of negative values (type 3): the value <c>v &lt; 0</c> is <c>-v - 2^(B-1) - 1</c>.</summary>
    public const int BinaryComplement = 3;

    /// <summary>Parses the content of an NLT marker segment.</summary>
    /// <param name="content">The content of the marker segment, after its length.</param>
    /// <param name="componentCount">The number of components of the image.</param>
    /// <param name="component">Receives the component, or -1 when the segment applies to every component.</param>
    /// <exception cref="InvalidImageContentException">The marker segment is malformed.</exception>
    public static Jpeg2000NonLinearity Read(ReadOnlySpan<byte> content, int componentCount, out int component)
    {
        if (content.Length < 4)
            throw Jpeg2000Format.Invalid("the NLT marker segment is too short.");

        var index = BinaryPrimitives.ReadUInt16BigEndian(content);
        if (index == 0xFFFF)
        {
            component = -1;
        }
        else if (index < componentCount)
        {
            component = index;
        }
        else
        {
            throw Jpeg2000Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"an NLT marker segment names the component {index} of an image of {componentCount}."));
        }

        var depth = content[2];
        var type = content[3];

        // Types 0 and 3 have no parameter; the gamma and the lookup table of types 1 and 2 have theirs
        if (type is 0 or BinaryComplement && content.Length != 4)
            throw Jpeg2000Format.Invalid("the length of an NLT marker segment is not the one of its type.");

        var precision = (depth & 0x7F) + 1;
        if (precision > 38)
            throw Jpeg2000Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"an NLT marker segment declares samples of {precision} bits."));

        return new Jpeg2000NonLinearity(precision, (depth & 0x80) != 0, type);
    }
}
