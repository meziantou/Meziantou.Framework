using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Sample expansions shared by the TGA decoder and encoder.</summary>
internal static class TgaSamples
{
    private static readonly byte[] Scale5 = CreateScale5();

    /// <summary>Expands a 5-bit channel of a 15/16-bit TGA sample to 8 bits (<c>round(value * 255 / 31)</c>).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte Expand5(int value) => Scale5[value];

    private static byte[] CreateScale5()
    {
        var scale = new byte[32];
        for (var value = 0; value < scale.Length; value++)
        {
            scale[value] = (byte)(((value * 255) + 15) / 31);
        }

        return scale;
    }
}
