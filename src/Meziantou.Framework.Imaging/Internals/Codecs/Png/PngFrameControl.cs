using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The fields of an APNG <c>fcTL</c> chunk, validated against the canvas.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct PngFrameControl(uint SequenceNumber, int Width, int Height, int X, int Y, ushort DelayNumerator, ushort DelayDenominator, byte DisposeOp, byte BlendOp)
{
    public const byte DisposeNone = 0;
    public const byte DisposeBackground = 1;
    public const byte DisposePrevious = 2;
    public const byte BlendSource = 0;
    public const byte BlendOver = 1;

    /// <summary>Gets the exact frame duration (a zero denominator means 1/100 s).</summary>
    public FrameDuration Duration => AnimationTiming.FromApngDelay(DelayNumerator, DelayDenominator);

    /// <summary>Gets a value indicating whether the frame region is the whole canvas.</summary>
    public bool CoversCanvas(int canvasWidth, int canvasHeight) => X == 0 && Y == 0 && Width == canvasWidth && Height == canvasHeight;
}
