namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Controls when the container image is pulled from its registry.</summary>
public enum PullPolicy
{
    /// <summary>Pull the image only when it is not already present locally.</summary>
    IfMissing,

    /// <summary>Always pull the image, even when a local copy exists.</summary>
    Always,

    /// <summary>Never pull the image; fail if it is not present locally.</summary>
    Never,
}
