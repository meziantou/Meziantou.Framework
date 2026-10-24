namespace Meziantou.Framework.Pdf.Rendering.Shadings;

/// <summary>A vertex of a mesh: its position in shading space and its color (or the input of the function of the shading).</summary>
internal sealed record MeshVertex(double X, double Y, double[] Color);
