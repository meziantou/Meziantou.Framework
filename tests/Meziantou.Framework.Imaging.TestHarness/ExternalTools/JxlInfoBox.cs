namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>A box of a JPEG XL container, as <c>jxlinfo -v</c> lists it.</summary>
/// <param name="Type">The four characters of the box type.</param>
/// <param name="Size">The size of the box, header included.</param>
public sealed record JxlInfoBox(string Type, long Size);
