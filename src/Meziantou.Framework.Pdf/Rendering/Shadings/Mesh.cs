namespace Meziantou.Framework.Pdf.Rendering.Shadings;

/// <summary>The triangles of a mesh shading, and the domain of its function.</summary>
internal sealed class Mesh(double[] domain)
{
    public double[] Domain { get; } = domain;

    public List<MeshTriangle> Triangles { get; } = [];
}
