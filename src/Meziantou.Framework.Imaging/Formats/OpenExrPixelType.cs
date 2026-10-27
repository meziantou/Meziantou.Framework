namespace Meziantou.Framework.Imaging.Formats;

/// <summary>The type of the samples written by <see cref="OpenExrEncoder"/>.</summary>
public enum OpenExrPixelType
{
    /// <summary>16-bit floating-point samples (<c>HALF</c>).</summary>
    Half = 0,

    /// <summary>32-bit floating-point samples (<c>FLOAT</c>).</summary>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "The name of the pixel type in the OpenEXR file format.")]
    Float = 1,
}
