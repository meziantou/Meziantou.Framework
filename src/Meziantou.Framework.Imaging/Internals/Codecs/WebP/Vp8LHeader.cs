using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The VP8L header fields.</summary>
/// <param name="Width">The image width (1 to 16,384).</param>
/// <param name="Height">The image height (1 to 16,384).</param>
/// <param name="AlphaIsUsed">The <c>alpha_is_used</c> hint.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct Vp8LHeader(int Width, int Height, bool AlphaIsUsed);
