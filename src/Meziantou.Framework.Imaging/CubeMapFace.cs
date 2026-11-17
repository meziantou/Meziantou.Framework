namespace Meziantou.Framework.Imaging;

/// <summary>A face of a cube map, in the order Direct3D stores them.</summary>
public enum CubeMapFace
{
    /// <summary>The face toward +X.</summary>
    PositiveX = 0,

    /// <summary>The face toward −X.</summary>
    NegativeX = 1,

    /// <summary>The face toward +Y.</summary>
    PositiveY = 2,

    /// <summary>The face toward −Y.</summary>
    NegativeY = 3,

    /// <summary>The face toward +Z.</summary>
    PositiveZ = 4,

    /// <summary>The face toward −Z.</summary>
    NegativeZ = 5,
}
