namespace Meziantou.Framework.Pdf;

/// <summary>The device color spaces (ISO 32000-2, 8.6.4).</summary>
public enum PdfDeviceColorSpace
{
    /// <summary><c>DeviceGray</c>: one component.</summary>
    Gray,

    /// <summary><c>DeviceRGB</c>: three components.</summary>
    Rgb,

    /// <summary><c>DeviceCMYK</c>: four components.</summary>
    Cmyk,
}
