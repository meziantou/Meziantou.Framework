namespace Meziantou.Framework.Imaging;

/// <summary>The result of <see cref="ProjectiveTransform.EstimateHomographyRobust(ReadOnlySpan{PointD}, ReadOnlySpan{PointD}, HomographyRansacOptions?)"/>: the homography and the correspondences it explains.</summary>
public sealed class HomographyEstimate
{
    private readonly bool[] _inliers;

    internal HomographyEstimate(ProjectiveTransform transform, bool[] inliers, int inlierCount, double rootMeanSquareError)
    {
        Transform = transform;
        _inliers = inliers;
        InlierCount = inlierCount;
        RootMeanSquareError = rootMeanSquareError;
    }

    /// <summary>Gets the homography, from the source points to the destination points.</summary>
    public ProjectiveTransform Transform { get; }

    /// <summary>Gets, for each correspondence, whether it is an inlier: its source point lands within the threshold of its destination point.</summary>
    public IReadOnlyList<bool> Inliers => _inliers;

    /// <summary>Gets the number of inliers.</summary>
    public int InlierCount { get; }

    /// <summary>Gets the root mean square of the distances between the destination points of the inliers and the images of their source points.</summary>
    public double RootMeanSquareError { get; }
}
