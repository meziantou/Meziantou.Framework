namespace Meziantou.Framework.Imaging;

/// <summary>Identifies the resource limit that was exceeded. Each value corresponds to a property of <see cref="ImageResourceLimits"/>.</summary>
public enum ImageResourceLimitKind
{
    /// <summary>The limit is not specified.</summary>
    Unknown = 0,

    /// <summary><see cref="ImageResourceLimits.MaxWidth"/>.</summary>
    Width = 1,

    /// <summary><see cref="ImageResourceLimits.MaxHeight"/>.</summary>
    Height = 2,

    /// <summary><see cref="ImageResourceLimits.MaxFramePixels"/>.</summary>
    FramePixels = 3,

    /// <summary><see cref="ImageResourceLimits.MaxFrames"/>.</summary>
    Frames = 4,

    /// <summary><see cref="ImageResourceLimits.MaxTotalPixels"/>.</summary>
    TotalPixels = 5,

    /// <summary><see cref="ImageResourceLimits.MaxEncodedBytes"/>.</summary>
    EncodedBytes = 6,

    /// <summary><see cref="ImageResourceLimits.MaxMetadataBytes"/>.</summary>
    MetadataBytes = 7,

    /// <summary><see cref="ImageResourceLimits.MaxLiveAllocationBytes"/>.</summary>
    LiveAllocationBytes = 8,
}
