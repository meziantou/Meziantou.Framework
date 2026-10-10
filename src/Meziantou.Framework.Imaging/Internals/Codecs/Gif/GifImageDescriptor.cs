using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A GIF image descriptor (logical-screen coordinates; the region may extend beyond the screen).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct GifImageDescriptor(int Left, int Top, int Width, int Height, bool IsInterlaced, int LocalColorTableEntries)
{
    public bool HasLocalColorTable => LocalColorTableEntries > 0;

    public bool CoversCanvas(int canvasWidth, int canvasHeight) => Left == 0 && Top == 0 && Width >= canvasWidth && Height >= canvasHeight;
}
