namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>A metadata box <c>jxlinfo</c> recognized.</summary>
/// <param name="Type">The four characters of the type of the metadata (the type inside a <c>brob</c> box).</param>
/// <param name="IsBrotliCompressed">Whether the box is a <c>brob</c> box.</param>
/// <param name="Length">The length jxlinfo prints for the box.</param>
public sealed record JxlInfoMetadata(string Type, bool IsBrotliCompressed, long Length);
