using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Represents a mount attached to a container. Implemented by <see cref="BindMount"/>, <see cref="VolumeMount"/>, and <see cref="TmpfsMount"/>.</summary>
[SuppressMessage("Design", "CA1040:Avoid empty interfaces", Justification = "Marker interface for the closed set of supported mount types.")]
public interface IMount;
