using Meziantou.Framework.Imaging;

namespace Meziantou.Framework.FileConversionTool.Inputs;

/// <summary>The bytes of an image file and its format.</summary>
internal sealed record EncodedImage(ReadOnlyMemory<byte> Data, ImageFormat Format);
