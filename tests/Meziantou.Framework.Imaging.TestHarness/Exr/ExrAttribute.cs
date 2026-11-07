namespace Meziantou.Framework.Imaging.TestHarness.Exr;

#pragma warning disable CA1819 // Properties should not return arrays
/// <summary>An attribute of an OpenEXR header.</summary>
/// <param name="Name">The attribute name.</param>
/// <param name="Type">The type name.</param>
/// <param name="Value">The value bytes.</param>
public sealed record ExrAttribute(string Name, string Type, byte[] Value);
