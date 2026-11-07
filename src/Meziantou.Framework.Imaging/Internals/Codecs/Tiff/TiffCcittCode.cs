using System.Runtime.InteropServices;

// Compiled into Meziantou.Framework.Imaging and Meziantou.Framework.Pdf (CCITTFaxDecode, JBIG2), internal to each. The copy
// of Pdf is in its namespace, so that Pdf does not depend on the internals of Imaging.
#if MEZIANTOU_FRAMEWORK_PDF
namespace Meziantou.Framework.Pdf.Filters;
#else
namespace Meziantou.Framework.Imaging.Internals;
#endif

/// <summary>One code word of the CCITT facsimile compressions.</summary>
/// <param name="Bits">The bits of the code, in the least significant positions; the most significant of them comes first.</param>
/// <param name="Length">The number of bits.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct TiffCcittCode(uint Bits, int Length);
