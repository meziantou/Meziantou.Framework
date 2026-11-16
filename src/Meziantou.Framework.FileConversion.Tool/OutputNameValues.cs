namespace Meziantou.Framework.FileConversionTool;

/// <summary>The values of the placeholders of an <see cref="OutputNameTemplate"/>.</summary>
/// <param name="Name">The name of the input file, without its extension.</param>
/// <param name="Extension">The extension of the output format, without its leading period.</param>
/// <param name="Directory">The folder of the input file.</param>
/// <param name="Page">The 1-based number of the page in its input.</param>
/// <param name="PageCount">The number of pages of the input, or null when it is not known.</param>
/// <param name="Index">The 1-based number of the output file.</param>
/// <param name="IndexCount">The number of output files, or null when it is not known.</param>
internal sealed record OutputNameValues(string Name, string Extension, string Directory, int Page, int? PageCount, int Index, int? IndexCount);
