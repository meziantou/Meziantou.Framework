using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>One channel of a <c>cdef</c> box (T.800, I.5.3.6).</summary>
/// <param name="Channel">The index of the channel (Cn).</param>
/// <param name="Type">The type of the channel (Typ).</param>
/// <param name="Association">The color the channel is associated with (Asoc).</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct Jpeg2000ChannelDefinition(int Channel, int Type, int Association)
{
    /// <summary>Typ 0: the channel is the color <see cref="Association"/>.</summary>
    public const int TypeColor = 0;

    /// <summary>Typ 1: the channel is an opacity.</summary>
    public const int TypeOpacity = 1;

    /// <summary>Typ 2: the channel is an opacity the colors were multiplied by.</summary>
    public const int TypePremultipliedOpacity = 2;

    /// <summary>Asoc 0: the channel is associated with the whole image.</summary>
    public const int AssociationImage = 0;
}
