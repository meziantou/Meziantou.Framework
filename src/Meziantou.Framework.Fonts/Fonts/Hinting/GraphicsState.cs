using System.Runtime.InteropServices;

namespace Meziantou.Framework.Fonts;

/// <summary>The graphics state of the TrueType interpreter (OpenType specification, "The Graphics State").</summary>
[StructLayout(LayoutKind.Auto)]
internal struct GraphicsState
{
    /// <summary>Gets the state the specification gives before any program runs.</summary>
    public static GraphicsState Default => new()
    {
        ProjectionX = 0x4000,
        DualX = 0x4000,
        FreedomX = 0x4000,
        Zp0 = 1,
        Zp1 = 1,
        Zp2 = 1,
        Loop = 1,
        MinimumDistance = 64,
        RoundMode = RoundMode.Grid,
        RoundPeriod = 64,
        RoundThreshold = 32,
        ControlValueCutIn = 68,
        DeltaBase = 9,
        DeltaShift = 3,
        AutoFlip = true,
    };

    public int ProjectionX;
    public int ProjectionY;
    public int DualX;
    public int DualY;
    public int FreedomX;
    public int FreedomY;
    public int Rp0;
    public int Rp1;
    public int Rp2;
    public int Zp0;
    public int Zp1;
    public int Zp2;
    public int Loop;
    public int MinimumDistance;
    public RoundMode RoundMode;
    public int RoundPeriod;
    public int RoundPhase;
    public int RoundThreshold;
    public int ControlValueCutIn;
    public int SingleWidthCutIn;
    public int SingleWidthValue;
    public int DeltaBase;
    public int DeltaShift;
    public bool AutoFlip;

    /// <summary>The flags INSTCTRL sets: 1, the glyph programs are not run; 2, they start from the default state.</summary>
    public int InstructControl;

    /// <summary>
    /// Resets what a glyph program never inherits from the control value program: the vectors, the reference points,
    /// the zone pointers and the loop. The other variables keep the values the control value program set.
    /// </summary>
    public void ResetForGlyph()
    {
        ProjectionX = DualX = FreedomX = 0x4000;
        ProjectionY = DualY = FreedomY = 0;
        Rp0 = Rp1 = Rp2 = 0;
        Zp0 = Zp1 = Zp2 = 1;
        Loop = 1;
    }
}
