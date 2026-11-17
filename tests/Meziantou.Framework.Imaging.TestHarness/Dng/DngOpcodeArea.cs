namespace Meziantou.Framework.Imaging.TestHarness.Dng;

/// <summary>The area, planes and pitches of the opcodes that modify part of an image.</summary>
public sealed record DngOpcodeArea(int Top, int Left, int Bottom, int Right, int Plane = 0, int Planes = 1, int RowPitch = 1, int ColumnPitch = 1)
{
    internal DngOpcodeWriter.Parameters Write(DngOpcodeWriter.Parameters parameters)
        => parameters.WriteInt32(Top).WriteInt32(Left).WriteInt32(Bottom).WriteInt32(Right).WriteInt32(Plane).WriteInt32(Planes).WriteInt32(RowPitch).WriteInt32(ColumnPitch);
}
